using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SprayFactory
{
    internal static class FactoryEffects
    {
        private sealed class Batch
        {
            internal readonly List<uint> Ids=new List<uint>();
            internal Material Material;
            internal ComputeBuffer IdBuffer,Args;
            internal int Capacity;
            internal void Free()
            {
                IdBuffer?.Release();Args?.Release();
                if(Material!=null) UnityEngine.Object.Destroy(Material);
            }
        }
        private sealed class Group {internal Batch Liquid,Spray;}
        private static readonly Dictionary<int,Group> groups=new Dictionary<int,Group>();
        private static readonly Plane[] planes=new Plane[6];
        private static readonly uint[] args=new uint[5];
        private static readonly System.Reflection.FieldInfo stateBuffer=HarmonyLib.AccessTools.Field(typeof(LabRenderer),"stateBuffer");
        private static int liquidSlot=-1,spraySlot=-1;

        internal static void Draw(PlanetFactory factory,Camera camera)
        {
            var model=factory?.planet?.factoryModel;
            if(model==null || camera==null || model.gpuiManager==null || model.disableFactoryEntities || !model.gpuiManager.renderEntity) return;
            Draw(factory,camera,model.gpuiManager.GetObjectRenderer(Prototypes.ModelId),model.entityAnimBuffer);
        }
        private static Batch CreateBatch(Material original,Color color,bool spray)
        {
            var material=new Material(original);color.a=1;
            foreach(string property in new[]{"_Color1","_Color2","_Color4"}) if(material.HasProperty(property)) material.SetColor(property,color);
            if(spray) {
                material.SetColor("_TintColor",new Color(1,1,1,0.5f));
                material.SetFloat("_SprayBrightness",0.5f);material.SetFloat("_SprayBrightnessBg",0.05f);
            } else {
                material.SetFloat("_EmissionSwitch",1);material.SetFloat("_EmissionMultiplier",0.25f);
            }
            return new Batch {Material=material,Args=new ComputeBuffer(5,4,ComputeBufferType.IndirectArguments)};
        }
        private static Group GetGroup(int item,PrefabDesc source)
        {
            if(groups.TryGetValue(item,out var group)) return group;
            var color=RecipeColors.ForItem(item);
            group=new Group {Liquid=CreateBatch(source.lodMaterials[0][liquidSlot],color,false),Spray=CreateBatch(source.lodMaterials[0][spraySlot],color,true)};
            groups.Add(item,group);return group;
        }
        internal static int Draw(PlanetFactory factory,Camera camera,ObjectRenderer renderer,ComputeBuffer animations)
        {
            if(factory?.cargoTraffic==null || camera==null || renderer==null || animations==null) return 0;
            var source=LDB.items.Select(2313).prefabDesc;
            if(liquidSlot<0 || spraySlot<0) for(int i=0;i<source.lodMaterials[0].Length;i++) {
                var name=source.lodMaterials[0][i]?.name;
                if(name==null) continue;
                if(name.IndexOf("water",StringComparison.OrdinalIgnoreCase)>=0) liquidSlot=i;
                if(name.IndexOf("effect",StringComparison.OrdinalIgnoreCase)>=0) spraySlot=i;
            }
            if(liquidSlot<0 || spraySlot<0) return 0;
            foreach(var group in groups.Values) {group.Liquid.Ids.Clear();group.Spray.Ids.Clear();}
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            var traffic=factory.cargoTraffic;int count=0;
            for(int i=1;i<traffic.spraycoaterCursor;i++) {
                var component=traffic.spraycoaterPool[i];
                if(component.id!=i || !FactoryRuntime.IsFactory(factory,component.entityId)
                    || !FactoryStore.TryGet(factory.planetId,component.entityId,out var state)) continue;
                var entity=factory.entityPool[component.entityId];
                if(entity.modelId<=0 || entity.modelId>=renderer.instCursor || renderer.instPool[entity.modelId].objId!=entity.id
                    || (entity.pos-camera.transform.position).sqrMagnitude>10000
                    || !GeometryUtility.TestPlanesAABB(planes,new Bounds(entity.pos,Vector3.one*6))) continue;
                int recipe,sprayItem;
                lock(state.Sync) {recipe=state.RecipeId;sprayItem=state.SprayTicks>0?state.SprayItemId:0;}
                var proto=LDB.recipes.Select(recipe);
                if(proto!=null && proto.Results.Length>0) {GetGroup(proto.Results[0],source).Liquid.Ids.Add((uint)entity.modelId);count++;}
                if(sprayItem>0) GetGroup(sprayItem,source).Spray.Ids.Add((uint)entity.modelId);
            }
            var batch=renderer.lodBatches[0];
            var states=renderer is LabRenderer?stateBuffer?.GetValue(renderer) as ComputeBuffer:null;
            foreach(var group in groups.Values) {
                Render(group.Liquid,liquidSlot,batch,animations,states,camera);
                Render(group.Spray,spraySlot,batch,animations,states,camera);
            }
            return count;
        }
        private static void Render(Batch group,int slot,BatchRenderer batch,ComputeBuffer animations,ComputeBuffer states,Camera camera)
        {
            if(group.Ids.Count==0) return;
            if(group.Capacity<group.Ids.Count) {
                group.IdBuffer?.Release();group.Capacity=Mathf.NextPowerOfTwo(group.Ids.Count);
                group.IdBuffer=new ComputeBuffer(group.Capacity,4);
            }
            group.IdBuffer.SetData(group.Ids);
            args[0]=batch.mesh.GetIndexCount(slot);args[1]=(uint)group.Ids.Count;args[2]=batch.mesh.GetIndexStart(slot);args[3]=batch.mesh.GetBaseVertex(slot);args[4]=0;
            group.Args.SetData(args);
            group.Material.SetBuffer("_InstBuffer",batch.instBuffer);group.Material.SetBuffer("_IdBuffer",group.IdBuffer);group.Material.SetBuffer("_AnimBuffer",animations);
            if(states!=null) group.Material.SetBuffer("_StateBuffer",states);
            batch.vertaBuffer?.SetToAnimMaterial(group.Material);
            // Color batches preserve native geometry and animation while selecting their own instances.
            Graphics.DrawMeshInstancedIndirect(batch.mesh,slot,group.Material,new Bounds(Vector3.zero,Vector3.one*10000),group.Args,0,null,ShadowCastingMode.Off,false,0,camera);
        }
        internal static void Clear()
        {
            foreach(var group in groups.Values) {group.Liquid.Free();group.Spray.Free();}
            groups.Clear();liquidSlot=spraySlot=-1;
        }
    }
}
