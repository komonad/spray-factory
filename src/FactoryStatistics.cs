using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SprayFactory
{
    internal static class FactoryRates
    {
        internal static void Calculate(FactoryState state, RecipeProto recipe, out float cycles, out float multiplier)
        {
            int level = recipe.Items.Length == 0 ? 0 : Cargo.accTableMilli.Length - 1;
            var buffers = state.Pending.Count > 0 ? state.Pending : state.Input;
            foreach (int item in recipe.Items)
            {
                int points = buffers.TryGetValue(item, out var buffer) && buffer.Count > 0 ? buffer.Inc / buffer.Count : 0;
                level = Math.Min(level, points);
            }
            level = Math.Max(0, Math.Min(level, Cargo.accTableMilli.Length - 1));
            bool extra = recipe.productive && !state.ForceAccMode;
            int duration = Math.Max(1, recipe.TimeSpend);
            if (!extra) duration = Math.Max(1, (int)Math.Ceiling(duration / (1 + Cargo.accTableMilli[level])));
            cycles = 3600f / duration;
            multiplier = extra ? 1f + (float)Cargo.incTableMilli[Math.Min(level,Cargo.incTableMilli.Length-1)] : 1f;
        }
    }

    [HarmonyPatch(typeof(ProductionExtraInfoCalculator), "CalculateFactory")]
    internal static class FactoryStatistics
    {
        private static void Postfix(ProductionExtraInfoCalculator __instance, GameData gameData, PlanetFactory factory)
        {
            var stat = __instance.productionStatistics?.factoryStatPool?[factory.index];
            if (stat == null || factory.cargoTraffic == null) return;
            Add(factory, gameData.history, stat);
        }

        internal static void Add(PlanetFactory factory, GameHistoryData history, FactoryProductionStat stat)
        {
            var traffic = factory.cargoTraffic;
            for (int i=1;i<traffic.spraycoaterCursor;i++)
            {
                var component=traffic.spraycoaterPool[i];
                if(component.id!=i || !FactoryRuntime.IsFactory(factory,component.entityId) ||
                    !FactoryStore.TryGet(factory.planetId,component.entityId,out var state)) continue;
                lock(state.Sync)
                {
                    var recipe=LDB.recipes.Select(state.RecipeId);
                    if(recipe!=null && history.RecipeUnlocked(recipe.ID))
                    {
                        FactoryRates.Calculate(state,recipe,out float cycles,out float multiplier);
                        for(int n=0;n<recipe.Items.Length;n++) stat.AddRefConsumeSpeed(recipe.Items[n],cycles*recipe.ItemCounts[n]);
                        for(int n=0;n<recipe.Results.Length;n++) stat.AddRefProductSpeed(recipe.Results[n],cycles*multiplier*recipe.ResultCounts[n]);
                    }
                    AddStorage(stat,state.Input);
                    AddStorage(stat,state.Output);
                    AddStorage(stat,state.Pending);
                }
            }
        }

        private static void AddStorage(FactoryProductionStat stat, Dictionary<int,ItemBuffer> buffers)
        {
            foreach(var entry in buffers)
                if(entry.Value.Count>0)
                {
                    stat.AddStorageCount(entry.Key,entry.Value.Count);
                    stat.AddDetailedStorageCount(entry.Key,EDetailedStorageCountType.Other,entry.Value.Count);
                }
        }
    }
}
