using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using xiaoye97;

namespace SprayFactory
{
    public static class Prototypes
    {
        public static int ItemId = 9455;
        public static int RecipeId = 9455;
        public static int ModelId = 727;
        public const int Marker = 1048576;
        public const long WorkEnergyPerTick = 5000;
        public const long IdleEnergyPerTick = 167;
        public static bool Ready { get; private set; }
        private static readonly MethodInfo Memberwise = AccessTools.Method(typeof(object), "MemberwiseClone");
        private static T Copy<T>(T source) { return (T)Memberwise.Invoke(source, null); }

        internal static void Register()
        {
            var occupiedItems = new HashSet<int>(LDB.items.dataArray.Concat(Queued<ItemProto>()).Select(x => x.ID));
            var occupiedRecipes = new HashSet<int>(LDB.recipes.dataArray.Concat(Queued<RecipeProto>()).Select(x => x.ID));
            while (occupiedItems.Contains(ItemId)) ItemId++;
            while (occupiedRecipes.Contains(RecipeId)) RecipeId++;
            if (ItemId >= 12000 || RecipeId >= 12000) throw new InvalidOperationException("No free Spray Factory prototype ID.");
            var occupiedModels = new HashSet<int>(LDB.models.dataArray.Concat(Queued<ModelProto>()).Select(x => x.ID));
            if (ModelId >= LDB.models.Length + 64 || occupiedModels.Contains(ModelId))
                ModelId = Enumerable.Range(1, LDB.models.Length + 63).Last(id => !occupiedModels.Contains(id));
            var sourceItem = LDB.items.Select(2313);
            var sourceModel = LDB.models.Select(sourceItem.ModelIndex);
            var source = sourceModel.prefabDesc;
            var model = Copy(sourceModel);
            model.ID = ModelId; model.Name = "SprayFactory.Model"; model.SID = model.sid = "";
            model.prefabDesc = new PrefabDesc(ModelId, source.prefab, source.colliderPrefab);
            foreach (var field in typeof(PrefabDesc).GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (!field.IsInitOnly) field.SetValue(model.prefabDesc, field.GetValue(source));
            var desc = model.prefabDesc;
            desc.modelIndex = ModelId;
            desc.isPowerConsumer = true;
            desc.workEnergyPerTick = WorkEnergyPerTick;
            desc.idleEnergyPerTick = IdleEnergyPerTick;
            desc.incCapacity = Marker;
            desc.incItemId = new int[0];
            desc.signHeight = Math.Max(2.2f, source.signHeight);
            desc.signSize = Math.Max(0.75f, source.signSize);
            desc.lodMaterials = CloneMaterials(source.lodMaterials, source.lodMaterials);
            desc.lodBlueprintMaterials = CloneMaterials(source.lodBlueprintMaterials, source.lodMaterials);
            LDBTool.PreAddProto(model);
            ModelId = model.ID;

            var item = Copy(sourceItem);
            item.ID = ItemId; item.Name = "SprayFactory.Name"; item.Description = "SprayFactory.Description";
            item.SID = item.sid = ""; item.ModelIndex = ModelId; item.ModelCount = 1;
            item.IconPath = "";
            item.Grade = 1; item.Upgrades = new int[0]; item.PreTechOverride = 0;
            item.BuildIndex = 0;
            item.GridIndex = FindGridSlot(sourceItem.GridIndex / 1000);
            item.handcrafts = item.recipes = item.makes = null; item.rawMats = null;
            LDBTool.PreAddProto(item);
            ItemId = item.ID;
            var recipe = new RecipeProto {
                ID = RecipeId, Name = "SprayFactory.Name", Description = "SprayFactory.Description", Type = ERecipeType.Assemble,
                IconPath = "", GridIndex = item.GridIndex, TimeSpend = 180,
                Items = new[] {1101,1201,1301}, ItemCounts = new[] {4,2,2}, Results = new[] {ItemId}, ResultCounts = new[] {1},
                Handcraft = true, NonProductive = true
            };
            LDBTool.PreAddProto(recipe);
            RecipeId = recipe.ID;
            Plugin.Log.LogInfo("Registered Spray Factory: item=" + ItemId + " recipe=" + RecipeId + " model=" + ModelId + " build=" + item.BuildIndex + " grid=" + item.GridIndex);
        }
        private static int FindGridSlot(int page)
        {
            var used = new HashSet<int>(LDB.items.dataArray.Concat(Queued<ItemProto>()).Select(x => x.GridIndex)
                .Concat(LDB.recipes.dataArray.Concat(Queued<RecipeProto>()).Select(x => x.GridIndex)));
            for (int row = 1; row <= 7; row++)
                for (int column = 1; column <= 14; column++)
                    if (!used.Contains(page * 1000 + row * 100 + column)) return page * 1000 + row * 100 + column;
            throw new InvalidOperationException("No free recipe grid slot.");
        }
        private static IEnumerable<T> Queued<T>() where T:Proto
        {
            foreach(string name in new[]{"PreToAdd","PostToAdd"})
            {
                var groups=AccessTools.Field(typeof(LDBTool),name)?.GetValue(null) as IEnumerable;
                if(groups==null) continue;
                foreach(IEnumerable group in groups)
                    foreach(object proto in group)
                        if(proto is T typed) yield return typed;
            }
        }
        private static Material[][] CloneMaterials(Material[][] source, Material[][] renderMaterials)
        {
            if (source == null) return null;
            return source.Select((row, lod) => row == null ? null : row.Select((original, slot) => {
                if (original == null) return null;
                var rendered = renderMaterials?[lod]?[slot];
                // Native GPU batches skip null material slots without changing submesh indices.
                if (rendered != null && (rendered.name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0
                    || rendered.name.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0
                    || rendered.shader.name.IndexOf("Spraycoater Effect", StringComparison.OrdinalIgnoreCase) >= 0)) return null;
                var material = new Material(original);
                bool water = material.name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0;
                var intensity = water ? 0.32f : 0.55f;
                var tint = new Color(intensity, intensity, intensity, 1);
                if (material.HasProperty("_Color4")) material.SetColor("_Color4", tint);
                if (material.HasProperty("_Color2")) material.SetColor("_Color2", tint);
                if (material.HasProperty("_EmissionMultiplier")) material.SetFloat("_EmissionMultiplier",0.25f);
                return material;
            }).ToArray()).ToArray();
        }
        internal static void Finish()
        {
            LDB.models.OnAfterDeserialize();
            ModelProto.InitMaxModelIndex(); ModelProto.InitModelIndices(); ModelProto.InitModelOrders();
            LDB.items.OnAfterDeserialize(); LDB.recipes.OnAfterDeserialize(); LDB.techs.OnAfterDeserialize();
            var factoryItem = LDB.items.Select(ItemId);
            var beltRecipe = LDB.recipes.dataArray.First(r => r.Results.Contains(2001));
            var unlockTech = LDB.techs.dataArray.First(t => t.UnlockRecipes != null && t.UnlockRecipes.Contains(beltRecipe.ID));
            factoryItem.PreTechOverride = unlockTech.ID;
            unlockTech.UnlockRecipes = unlockTech.UnlockRecipes.Concat(new[] { RecipeId }).Distinct().ToArray();
            for (int i = 0; i < LDB.items.dataArray.Length; i++)
            {
                var item = LDB.items.dataArray[i]; item.recipes = null; item.rawMats = null; item.Preload(i);
            }
            FactoryIcon.Apply();
            for (int i = 0; i < LDB.recipes.dataArray.Length; i++) LDB.recipes.dataArray[i].Preload(i);
            foreach (var tech in LDB.techs.dataArray) { tech.Preload(); tech.Preload2(); }
            PlanetFactory.PrefabDescByModelIndex = null; PlanetFactory.InitPrefabDescArray();
            SpaceSector.PrefabDescByModelIndex = null; SpaceSector.InitPrefabDescArray();
            FactoryBuildMenu.Place(factoryItem,LDB.items.Select(2313));
            if (GameMain.instance != null)
                foreach (string method in new[] { "CreateGPUInstancing", "CreateBPGPUInstancing", "CreateStarmapGPUInstancing" })
                    AccessTools.Method(typeof(GameMain), method)?.Invoke(GameMain.instance, null);
            Ready = true;
        }
    }
}
