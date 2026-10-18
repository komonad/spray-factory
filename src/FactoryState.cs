using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SprayFactory
{
    public sealed class ItemBuffer
    {
        public int Count;
        public int Inc;
        public void Add(int count, int inc) { Count = checked(Count + count); Inc = checked(Inc + inc); }
        public int Take(int count)
        {
            if (count < 0 || count > Count) throw new ArgumentOutOfRangeException(nameof(count));
            int inc = Count == 0 ? 0 : (int)((long)Inc * count / Count);
            Count -= count; Inc -= inc;
            return inc;
        }
    }

    public sealed class FactoryState
    {
        public readonly object Sync = new object();
        public readonly Dictionary<int, ItemBuffer> Input = new Dictionary<int, ItemBuffer>();
        public readonly Dictionary<int, ItemBuffer> Output = new Dictionary<int, ItemBuffer>();
        public readonly Dictionary<int, ItemBuffer> Pending = new Dictionary<int, ItemBuffer>();
        public int RecipeId;
        public int Progress;
        public int Duration;
        public double PowerProgress;
        public double ExtraProgress;
        public int OutputCursor;
        public bool ForceAccMode;
        public bool BatchAccMode;
        public int SprayTicks;
        public int SprayItemId;
        public string Status = "未选择配方";
        public static ItemBuffer Buffer(Dictionary<int, ItemBuffer> buffers, int item)
        {
            if (!buffers.TryGetValue(item, out ItemBuffer result)) buffers[item] = result = new ItemBuffer();
            return result;
        }
        public static int InputLimit(int recipeCount) { return checked(recipeCount * 2); }
        public static int ManualInputRounds(ERecipeType type) { return type == ERecipeType.Smelt ? 50 : 20; }
        public static int ManualInputLimit(int recipeCount, ERecipeType type) { return checked(recipeCount * ManualInputRounds(type)); }
        public static int OutputLimit(int recipeCount, int batches = 1)
        {
            // A complete batch must fit while the buffer holds less than one output stack.
            return Math.Max(200, Math.Max(checked(recipeCount * 20), checked(FactoryRuntime.OutputStackLimit() + recipeCount * batches - 1)));
        }
        public int Acceptable(RecipeProto recipe, int item)
        {
            if (recipe == null) return 0;
            int index = Array.IndexOf(recipe.Items, item);
            return index < 0 ? 0 : Math.Max(0, InputLimit(recipe.ItemCounts[index]) - Buffer(Input, item).Count);
        }
        public void ChangeRecipe(int recipeId)
        {
            if (recipeId == RecipeId) return;
            foreach (var pair in Pending) Buffer(Input, pair.Key).Add(pair.Value.Count, pair.Value.Inc);
            Pending.Clear(); Progress = Duration = 0; ExtraProgress = 0; RecipeId = recipeId;
            PowerProgress=0;
            SprayTicks = SprayItemId = 0;
        }
        public bool CanWork(RecipeProto recipe)
        {
            return recipe!=null && (Pending.Count>0 || Duration>0 || TryPlan(recipe,out _,out _));
        }
        private bool TryPlan(RecipeProto recipe,out int level,out double bonus)
        {
            level=0;bonus=0;
            for (int i = 0; i < recipe.Items.Length; i++)
                if (Buffer(Input, recipe.Items[i]).Count < recipe.ItemCounts[i])
                { Status = "缺少原料"; return false; }
            level = recipe.Items.Length > 0 ? 10 : 0;
            for (int i = 0; i < recipe.Items.Length; i++)
            {
                ItemBuffer input = Buffer(Input, recipe.Items[i]);
                level = Math.Min(level, input.Count == 0 ? 0 : input.Inc / input.Count);
            }
            level = Math.Max(0, Math.Min(level, Cargo.accTableMilli.Length - 1));
            bonus = recipe.productive && !ForceAccMode ? Cargo.incTableMilli[Math.Min(level, Cargo.incTableMilli.Length - 1)] : 0;
            int plannedBatches = 1 + (int)(ExtraProgress + bonus);
            for (int i = 0; i < recipe.Results.Length; i++)
                if ((long)Buffer(Output, recipe.Results[i]).Count + (long)recipe.ResultCounts[i] * plannedBatches > OutputLimit(recipe.ResultCounts[i], plannedBatches))
                { Status = "产物缓存已满"; return false; }
            return true;
        }
        public bool Tick(RecipeProto recipe, Action<int, int> consume, Action<int, int> produce,float power=1)
        {
            if (recipe == null) { Status = "未选择配方"; return false; }
            if(power<0.1f) { Status="未供电";return false; }
            if (Pending.Count == 0 && Duration == 0)
            {
                if(!TryPlan(recipe,out int level,out double bonus)) return false;
                bool extraProducts = recipe.productive && !ForceAccMode;
                for (int i = 0; i < recipe.Items.Length; i++)
                {
                    ItemBuffer input = Buffer(Input, recipe.Items[i]);
                    int count = recipe.ItemCounts[i];
                    Buffer(Pending, recipe.Items[i]).Add(count, input.Take(count));
                }
                Duration = Math.Max(1, recipe.TimeSpend);
                if (!extraProducts) Duration = Math.Max(1, (int)Math.Ceiling(Duration / (1 + Cargo.accTableMilli[level])));
                BatchBonus = bonus;
                BatchAccMode = !extraProducts;
            }
            Status = "生产中";
            PowerProgress+=Math.Max(0,Math.Min(1,power));
            if(PowerProgress<1) return true;
            PowerProgress-=1;
            if (++Progress < Duration) return true;
            ExtraProgress += BatchBonus;
            int batches = 1 + (int)ExtraProgress;
            ExtraProgress -= (int)ExtraProgress;
            for (int i = 0; i < recipe.Results.Length; i++)
            {
                int count = checked(recipe.ResultCounts[i] * batches);
                Buffer(Output, recipe.Results[i]).Add(count, 0);
                produce?.Invoke(recipe.Results[i], count);
            }
            foreach (var pair in Pending) consume?.Invoke(pair.Key, pair.Value.Count);
            Pending.Clear(); Progress = Duration = 0;
            return true;
        }
        public double BatchBonus;
        public void Write(BinaryWriter w)
        {
            w.Write(RecipeId); w.Write(Progress); w.Write(Duration); w.Write(ExtraProgress); w.Write(BatchBonus); w.Write(OutputCursor);
            WriteBuffers(w, Input); WriteBuffers(w, Output); WriteBuffers(w, Pending);
            w.Write(ForceAccMode);
            w.Write(BatchAccMode);
        }
        public static FactoryState Read(BinaryReader r, int version = 2)
        {
            var state = new FactoryState { RecipeId = r.ReadInt32(), Progress = r.ReadInt32(), Duration = r.ReadInt32(), ExtraProgress = r.ReadDouble(), BatchBonus = r.ReadDouble(), OutputCursor = r.ReadInt32() };
            if (state.Progress < 0 || state.Duration < 0 || state.Progress > state.Duration || double.IsNaN(state.ExtraProgress) || double.IsInfinity(state.ExtraProgress) || state.ExtraProgress < 0 || state.ExtraProgress >= 1 || double.IsNaN(state.BatchBonus) || double.IsInfinity(state.BatchBonus) || state.BatchBonus < 0 || state.BatchBonus > 10)
                throw new InvalidDataException("Invalid factory progress.");
            ReadBuffers(r, state.Input); ReadBuffers(r, state.Output); ReadBuffers(r, state.Pending);
            if (version >= 2) { state.ForceAccMode = r.ReadBoolean(); state.BatchAccMode = r.ReadBoolean(); }
            return state;
        }
        private static void WriteBuffers(BinaryWriter w, Dictionary<int, ItemBuffer> buffers)
        {
            var entries = buffers.Where(x => x.Value.Count > 0).OrderBy(x => x.Key).ToArray();
            w.Write(entries.Length);
            foreach (var pair in entries) { w.Write(pair.Key); w.Write(pair.Value.Count); w.Write(pair.Value.Inc); }
        }
        private static void ReadBuffers(BinaryReader r, Dictionary<int, ItemBuffer> buffers)
        {
            int size = r.ReadInt32();
            if (size < 0 || size > 12000) throw new InvalidDataException("Invalid factory buffer size.");
            for (int i = 0; i < size; i++)
            {
                int id = r.ReadInt32(), count = r.ReadInt32(), inc = r.ReadInt32();
                if (id <= 0 || count < 0 || inc < 0 || buffers.ContainsKey(id)) throw new InvalidDataException("Invalid factory buffer entry.");
                buffers.Add(id, new ItemBuffer { Count = count, Inc = inc });
            }
        }
    }

    public static class FactoryStore
    {
        private static readonly ConcurrentDictionary<long, FactoryState> states = new ConcurrentDictionary<long, FactoryState>();
        public static long Key(int planet, int entity) { return ((long)planet << 32) | (uint)entity; }
        public static FactoryState Get(int planet, int entity) { return states.GetOrAdd(Key(planet, entity), _ => new FactoryState()); }
        public static bool Remove(int planet, int entity, out FactoryState state) { return states.TryRemove(Key(planet, entity), out state); }
        public static bool TryGet(int planet, int entity, out FactoryState state) { return states.TryGetValue(Key(planet, entity), out state); }
        public static void Clear() { states.Clear(); }
        public static void Export(BinaryWriter w)
        {
            w.Write(2);
            var snapshot = states.OrderBy(x => x.Key).ToArray();
            w.Write(snapshot.Length);
            foreach (var pair in snapshot) { w.Write(pair.Key); lock (pair.Value.Sync) pair.Value.Write(w); }
        }
        public static void Import(BinaryReader r)
        {
            int version = r.ReadInt32();
            if (version != 1 && version != 2) throw new InvalidDataException("Unsupported Spray Factory save version.");
            int count = r.ReadInt32();
            if (count < 0 || count > 1000000) throw new InvalidDataException("Invalid factory count.");
            var loaded = new Dictionary<long, FactoryState>();
            for (int i = 0; i < count; i++) loaded.Add(r.ReadInt64(), FactoryState.Read(r,version));
            states.Clear();
            foreach (var pair in loaded) states[pair.Key] = pair.Value;
        }
    }
}
