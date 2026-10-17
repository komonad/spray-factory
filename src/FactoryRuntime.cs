using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SprayFactory
{
    public static class FactoryRuntime
    {
        public static bool IsFactory(PlanetFactory factory, int entity)
        {
            return factory != null && entity > 0 && entity < factory.entityPool.Length &&
                factory.entityPool[entity].id == entity && factory.entityPool[entity].protoId == Prototypes.ItemId;
        }
        public static int OutputStackLimit() { return Math.Max(1, Math.Min(255, GameMain.history?.stationPilerLevel ?? 1)); }
        public static void SetPower(ref SpraycoaterComponent component, PowerConsumerComponent[] consumers)
        {
            if (component.incCapacity != Prototypes.Marker) component.SetPCState(consumers);
            else if (component.pcId>0 && component.pcId<consumers.Length && consumers[component.pcId].id==component.pcId)
                consumers[component.pcId].SetRequiredEnergy(component.incUsed);
        }
        public static void UpdateSign(PlanetFactory factory, int entity, RecipeProto recipe, bool working)
        {
            if (factory.entitySignPool == null || entity <= 0 || entity >= factory.entitySignPool.Length) return;
            ref var sign = ref factory.entitySignPool[entity];
            if (sign.w <= 0)
            {
                var data = factory.entityPool[entity];
                var desc = LDB.items.Select(Prototypes.ItemId).prefabDesc;
                sign.Reset(data.pos, data.rot, desc.signHeight, desc.signSize);
            }
            sign.iconId0 = (uint)(recipe?.ID ?? 0);
            sign.iconType = recipe == null ? 0u : 2u;
            sign.signType = recipe == null ? SignData.NO_RECIPE : (working ? SignData.NONE : SignData.NOT_WORKING);
        }
        public static void Tick(ref SpraycoaterComponent component, CargoTraffic traffic, AnimData[] animations, int[] consumeRegister)
        {
            var factory = traffic.factory;
            var state = FactoryStore.Get(factory.planetId, component.entityId);
            lock (state.Sync)
            {
                if(state.SprayTicks>0) state.SprayTicks--;
                var recipe = LDB.recipes.Select(state.RecipeId);
                bool unlocked = recipe != null && GameMain.history != null && GameMain.history.RecipeUnlocked(recipe.ID);
                float power=FactoryPower.Ratio(factory.powerSystem,component.pcId);
                if (unlocked && power>=0.1f)
                {
                    Receive(traffic, component.incBeltId, state, recipe);
                }
                bool working = false;
                component.incUsed=unlocked && state.CanWork(recipe);
                if (unlocked && power>=0.1f)
                {
                    var productRegister = GameMain.statistics.production.factoryStatPool[factory.index].productRegister;
                    working = state.Tick(recipe,
                        (id, count) => { lock (consumeRegister) consumeRegister[id] += count; },
                        (id, count) => { lock (productRegister) productRegister[id] += count; },power);
                }
                if (!unlocked) state.Status = recipe == null ? "未选择配方" : "配方尚未解锁";
                else if(power<0.1f) state.Status="未供电";
                else if(working && power<0.999f) state.Status="供电不足";
                UpdateSign(factory, component.entityId, recipe, working);
                if(power<0.5f && factory.entitySignPool!=null)
                {
                    int network=component.pcId>0 && component.pcId<factory.powerSystem.consumerPool.Length
                        ?factory.powerSystem.consumerPool[component.pcId].networkId:0;
                    factory.entitySignPool[component.entityId].signType=network==0?SignData.NO_POWER_CONN
                        :power<0.1f?SignData.NO_POWER:SignData.LOW_POWER;
                }
                if(power>=0.1f) Send(traffic, component.cargoBeltId, state);
                if (animations != null)
                {
                    ref var anim = ref animations[component.entityId];
                    UpdateAnimation(ref anim,state,recipe);
                    anim.power=power;
                }
            }
        }
        public static void UpdateAnimation(ref AnimData animation,FactoryState state,RecipeProto recipe)
        {
            float capacity=0,stock=0;
            if(recipe!=null) for(int i=0;i<recipe.Items.Length;i++) {
                capacity+=FactoryState.InputLimit(recipe.ItemCounts[i]);
                if(state.Input.TryGetValue(recipe.Items[i],out var input)) stock+=input.Count;
                if(state.Pending.TryGetValue(recipe.Items[i],out var pending)) stock+=pending.Count;
            }
            // The native liquid shader selects its color from the tens digit of state.
            animation.state=recipe==null && state.SprayTicks==0?1u:41u;
            if(state.SprayTicks>0) animation.state+=100u+(uint)state.SprayTicks*1000u;
            animation.time=recipe==null?0:Mathf.Lerp(0.35f,0.9f,capacity>0?Mathf.Clamp01(stock/capacity):0);
            animation.power=1;
        }
        public static void Receive(CargoTraffic traffic, int belt, FactoryState state, RecipeProto recipe)
        {
            if (belt <= 0 || belt >= traffic.beltPool.Length || traffic.beltPool[belt].id != belt) return;
            var segment = traffic.beltPool[belt];
            var path = traffic.GetCargoPath(segment.segPathId);
            if (path == null) return;
            lock (path.buffer)
            {
                int pivot = segment.segIndex + segment.segPivotOffset;
                int cargoId = path.GetCargoIdAtIndex(pivot - 2, 5);
                if (cargoId < 0) return;
                ref var cargo = ref traffic.container.cargoPool[cargoId];
                if (cargo.stack <= 0) return;
                int accepted = Math.Min(state.Acceptable(recipe, cargo.item), cargo.stack);
                if (accepted <= 0) return;
                if (accepted < cargo.stack)
                {
                    int takenInc = cargo.inc * accepted / cargo.stack;
                    FactoryState.Buffer(state.Input, cargo.item).Add(accepted, takenInc);
                    cargo.stack -= (byte)accepted;
                    cargo.inc -= (byte)takenInc;
                    return;
                }
                byte stack, inc;
                int item = path.TryPickItem(pivot - 2, 5, cargo.item, out stack, out inc);
                if (item > 0) FactoryState.Buffer(state.Input, item).Add(stack, inc);
            }
        }
        public static void Send(CargoTraffic traffic, int belt, FactoryState state)
        {
            if (belt <= 0 || belt >= traffic.beltPool.Length || traffic.beltPool[belt].id != belt) return;
            int stack = OutputStackLimit();
            var entries = state.Output.Where(x => x.Value.Count >= stack).OrderBy(x => x.Key).ToArray();
            if (entries.Length == 0) return;
            int index = (state.OutputCursor & int.MaxValue) % entries.Length;
            var entry = entries[index];
            int inc = entry.Value.Count == 0 ? 0 : (int)((long)entry.Value.Inc * stack / entry.Value.Count);
            if (inc > 255) return;
            if (traffic.TryInsertItem(belt, 0, entry.Key, (byte)stack, (byte)Math.Min(255, inc)))
            {
                entry.Value.Take(stack);
                state.SprayTicks=45;state.SprayItemId=entry.Key;
                state.OutputCursor = (index + 1) % entries.Length;
            }
        }
        public static void ReturnItems(Player player, int entity, FactoryState state)
        {
            lock (state.Sync)
            {
                foreach (var group in new[] {state.Input, state.Output, state.Pending})
                    foreach (var pair in group)
                    {
                        // Overflow is transferred to the native trash system by TryAddItemToPackage.
                        player.TryAddItemToPackage(pair.Key, pair.Value.Count, pair.Value.Inc, true, entity);
                        pair.Value.Count = pair.Value.Inc = 0;
                    }
                state.Input.Clear(); state.Output.Clear(); state.Pending.Clear();
                state.Progress = state.Duration = 0;state.PowerProgress=0;
                state.SprayTicks = state.SprayItemId = 0;
            }
        }
    }

    [HarmonyPatch(typeof(SpraycoaterComponent), nameof(SpraycoaterComponent.InternalUpdate))]
    internal static class ProductionPatch
    {
        private static bool Prefix(ref SpraycoaterComponent __instance, CargoTraffic _traffic, AnimData[] _animPool, int[] consumeRegister)
        {
            if (!FactoryRuntime.IsFactory(_traffic.factory, __instance.entityId)) return true;
            FactoryRuntime.Tick(ref __instance, _traffic, _animPool, consumeRegister);
            return false;
        }
    }
    [HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.SpraycoaterGameTick))]
    internal static class PowerPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var source = AccessTools.Method(typeof(SpraycoaterComponent), nameof(SpraycoaterComponent.SetPCState));
            var target = AccessTools.Method(typeof(FactoryRuntime), nameof(FactoryRuntime.SetPower));
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(source)) { instruction.opcode = OpCodes.Call; instruction.operand = target; replaced++; }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Spraycoater power update call was not found exactly once.");
        }
    }
    [HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.TakeBackItems_Spraycoater))]
    internal static class ReturnPatch
    {
        private static bool Prefix(CargoTraffic __instance, Player player, int spraycoaterId)
        {
            int entity = __instance.spraycoaterPool[spraycoaterId].entityId;
            if (!FactoryRuntime.IsFactory(__instance.factory, entity)) return true;
            if (FactoryStore.TryGet(__instance.factory.planetId, entity, out var state))
            {
                FactoryRuntime.ReturnItems(player, entity, state);
                lock (state.Sync) state.Progress = state.Duration = 0;
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.ClearItems_Spraycoater))]
    internal static class ClearPatch
    {
        private static bool Prefix(CargoTraffic __instance, int spraycoaterId)
        {
            int entity = __instance.spraycoaterPool[spraycoaterId].entityId;
            if (!FactoryRuntime.IsFactory(__instance.factory, entity)) return true;
            if (FactoryStore.TryGet(__instance.factory.planetId, entity, out var state))
                lock (state.Sync) { state.Input.Clear(); state.Output.Clear(); state.Pending.Clear(); state.Progress = state.Duration = 0;state.PowerProgress=0; }
            return false;
        }
    }
    [HarmonyPatch(typeof(CargoTraffic), nameof(CargoTraffic.RemoveSpraycoaterComponent))]
    internal static class RemovePatch
    {
        private static void Prefix(CargoTraffic __instance, int id)
        {
            int entity = __instance.spraycoaterPool[id].entityId;
            if (!FactoryRuntime.IsFactory(__instance.factory, entity)) return;
            if (FactoryStore.Remove(__instance.factory.planetId, entity, out var state) && GameMain.mainPlayer != null)
                FactoryRuntime.ReturnItems(GameMain.mainPlayer, entity, state);
        }
    }
}
