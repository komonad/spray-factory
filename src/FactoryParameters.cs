using System;
using HarmonyLib;
using UnityEngine;

namespace SprayFactory
{
    internal static class FactoryParameters
    {
        internal static bool IsFactory(PlanetFactory factory,int id)
        {
            return id>0 ? FactoryRuntime.IsFactory(factory,id) : factory!=null && id<0 && -id<factory.prebuildPool.Length
                && factory.prebuildPool[-id].id==-id && factory.prebuildPool[-id].protoId==Prototypes.ItemId;
        }
        internal static int[] Encode(int recipe,bool accelerate) { return new[]{Prototypes.Marker,1,recipe,accelerate?1:0}; }
        internal static bool Decode(int[] data,out int recipe,out bool accelerate)
        {
            recipe=0; accelerate=false;
            if(data==null || data.Length<4 || data[0]!=Prototypes.Marker || data[1]!=1) return false;
            recipe=data[2]; accelerate=data[3]!=0; return true;
        }
        internal static void Read(PlanetFactory factory,int id,out int recipe,out bool accelerate)
        {
            recipe=0; accelerate=false;
            if(id<0)
            {
                recipe=factory.prebuildPool[-id].recipeId;
                if(Decode(factory.prebuildPool[-id].parameters,out int encoded,out accelerate)) recipe=encoded;
            }
            else if(FactoryStore.TryGet(factory.planetId,id,out var state))
                lock(state.Sync) {recipe=state.RecipeId;accelerate=state.ForceAccMode;}
        }
        internal static bool Allowed(PlanetFactory factory,int recipe)
        {
            return recipe==0 || LDB.recipes.Select(recipe)!=null && (factory.gameData?.history??GameMain.history)?.RecipeUnlocked(recipe)==true;
        }
        internal static void Apply(PlanetFactory factory,int id,int recipe,bool accelerate)
        {
            if(id<0)
            {
                factory.prebuildPool[-id].recipeId=recipe;
                factory.prebuildPool[-id].parameters=Encode(recipe,accelerate);
                factory.prebuildPool[-id].paramCount=4;
                return;
            }
            var state=FactoryStore.Get(factory.planetId,id);
            lock(state.Sync) {state.ChangeRecipe(recipe);state.ForceAccMode=accelerate;}
            FactoryRuntime.UpdateSign(factory,id,LDB.recipes.Select(recipe),false);
        }
    }
    [HarmonyPatch(typeof(BuildingParameters),nameof(BuildingParameters.CopyFromFactoryObject))]
    internal static class CopyFactoryParameters
    {
        private static bool Prefix(ref BuildingParameters __instance,int objectId,PlanetFactory factory,ref bool __result)
        {
            if(!FactoryParameters.IsFactory(factory,objectId)) return true;
            __instance.SetEmpty(); __instance.type=BuildingType.Other; __instance.itemId=Prototypes.ItemId;
            __instance.modelIndex=objectId>0?factory.entityPool[objectId].modelIndex:factory.prebuildPool[-objectId].modelIndex;
            var position=objectId>0?factory.entityPool[objectId].pos:factory.prebuildPool[-objectId].pos;
            var rotation=objectId>0?factory.entityPool[objectId].rot:factory.prebuildPool[-objectId].rot;
            __instance.yaw=Mathf.Round((Quaternion.Inverse(Maths.SphericalRotation(position,0))*rotation).eulerAngles.y/3)*3;
            FactoryParameters.Read(factory,objectId,out int recipe,out bool accelerate);
            __instance.recipeId=recipe; __instance.recipeType=LDB.recipes.Select(recipe)?.Type??ERecipeType.None;
            __instance.mode0=accelerate?1:0; __instance.parameters=FactoryParameters.Encode(recipe,accelerate);
            __result=true; return false;
        }
    }
    [HarmonyPatch(typeof(BuildingParameters),nameof(BuildingParameters.ToParamsArray))]
    internal static class EncodeFactoryParameters
    {
        private static bool Prefix(ref BuildingParameters __instance,ref int[] _parameters,ref int _paramCount)
        {
            if(__instance.itemId!=Prototypes.ItemId) return true;
            _parameters=FactoryParameters.Encode(__instance.recipeId,__instance.mode0!=0); _paramCount=4; return false;
        }
    }
    [HarmonyPatch(typeof(BuildingParameters),nameof(BuildingParameters.FromParamsArray))]
    internal static class DecodeFactoryParameters
    {
        private static bool Prefix(ref BuildingParameters __instance,int[] _parameters)
        {
            if(__instance.itemId!=Prototypes.ItemId) return true;
            if(FactoryParameters.Decode(_parameters,out int recipe,out bool accelerate))
            {
                __instance.recipeId=recipe; __instance.recipeType=LDB.recipes.Select(recipe)?.Type??ERecipeType.None;
                __instance.mode0=accelerate?1:0; __instance.parameters=(int[])_parameters.Clone();
            }
            return false;
        }
    }
    [HarmonyPatch(typeof(BuildingParameters),nameof(BuildingParameters.CanPasteToFactoryObject))]
    internal static class CanPasteFactoryParameters
    {
        private static bool Prefix(ref BuildingParameters __instance,int objectId,PlanetFactory factory,ref bool __result)
        {
            if(__instance.itemId!=Prototypes.ItemId && !FactoryParameters.IsFactory(factory,objectId)) return true;
            __result=__instance.itemId==Prototypes.ItemId && FactoryParameters.IsFactory(factory,objectId) && FactoryParameters.Allowed(factory,__instance.recipeId);
            return false;
        }
    }
    [HarmonyPatch(typeof(BuildingParameters),nameof(BuildingParameters.PasteToFactoryObject))]
    internal static class PasteFactoryParameters
    {
        private static bool Prefix(ref BuildingParameters __instance,int objectId,PlanetFactory factory,ref bool __result)
        {
            if(__instance.itemId!=Prototypes.ItemId && !FactoryParameters.IsFactory(factory,objectId)) return true;
            __result=__instance.itemId==Prototypes.ItemId && FactoryParameters.IsFactory(factory,objectId) && FactoryParameters.Allowed(factory,__instance.recipeId);
            if(__result) FactoryParameters.Apply(factory,objectId,__instance.recipeId,__instance.mode0!=0);
            return false;
        }
    }
    [HarmonyPatch(typeof(BuildingParameters),nameof(BuildingParameters.ApplyPrebuildParametersToEntity))]
    internal static class RestoreFactoryParameters
    {
        private static bool Prefix(int entityId,int recipeId,int[] parameters,PlanetFactory factory)
        {
            if(!FactoryRuntime.IsFactory(factory,entityId)) return true;
            bool accelerate=false;
            if(FactoryParameters.Decode(parameters,out int encoded,out accelerate)) recipeId=encoded;
            if(FactoryParameters.Allowed(factory,recipeId)) FactoryParameters.Apply(factory,entityId,recipeId,accelerate);
            return false;
        }
    }

    [HarmonyPatch(typeof(BuildTool_Addon),nameof(BuildTool_Addon.DeterminePreviews))]
    internal static class FactoryAddonPreview
    {
        private static void Postfix(BuildTool_Addon __instance)
        {
            if(__instance.handItem?.ID!=Prototypes.ItemId) return;
            var templates=__instance.actionBuild?.templatePreviews;
            if(templates==null || templates.Count==0 || templates[0].item?.ID!=Prototypes.ItemId) return;
            var template=templates[0];
            bool accelerate=false;
            int recipe=template.recipeId;
            if(FactoryParameters.Decode(template.parameters,out int encoded,out accelerate)) recipe=encoded;
            // Native addon placement discards spray coater templates before creating its previews.
            foreach(var preview in __instance.buildPreviews)
            {
                if(preview.item?.ID!=Prototypes.ItemId) continue;
                preview.recipeId=recipe;
                preview.SetParameters(4,FactoryParameters.Encode(recipe,accelerate));
            }
        }
    }
}
