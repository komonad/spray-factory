using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using SprayFactory;
using UnityEngine;

[BepInPlugin("local.sprayfactory.checks", "Spray Factory Checks", "1.0.0")]
[BepInDependency(Plugin.Guid)]
public sealed class RuntimeChecks : BaseUnityPlugin
{
    private bool started;
    private readonly List<string> results = new List<string>();
    private int failures;
    private string Output => Path.GetDirectoryName(Info.Location);
    private void Update()
    {
        if (started || !Prototypes.Ready || UIRoot.instance == null || LDB.items == null || LDB.items.dataArray == null) return;
        if (LDB.items.dataArray.All(x => x.ID != Prototypes.ItemId)) return;
        started = true; StartCoroutine(Run());
    }
    private void Check(bool condition, string name)
    {
        results.Add((condition ? "PASS " : "FAIL ") + name);
        if (!condition) failures++;
    }
    private void Test(string name, Action action)
    {
        try { action(); } catch (Exception ex) { failures++; results.Add("FAIL " + name + "\n" + ex); }
    }
    private IEnumerator Run()
    {
        yield return new WaitForSecondsRealtime(3);
        Test("vanilla splitter isolation", () => {
            var item=LDB.items.Select(2020);
            for(int m=0;m<item.ModelCount;m++)
            {
                var desc=LDB.models.Select(item.ModelIndex+m).prefabDesc;
                var native=desc.independentCollider?new PrefabDesc(desc.modelIndex,desc.prefab,desc.colliderPrefab):new PrefabDesc(desc.modelIndex,desc.prefab);
                Check(desc.buildCollider.Equals(native.buildCollider) && desc.buildColliders.SequenceEqual(native.buildColliders)
                    && desc.colliders.SequenceEqual(native.colliders) && desc.blueprintBoxSize==native.blueprintBoxSize
                    && desc.dragBuildDist==native.dragBuildDist && desc.portPoses.SequenceEqual(native.portPoses)
                    && desc.multiLevel==native.multiLevel && desc.multiLevelAllowPortsOrSlots==native.multiLevelAllowPortsOrSlots
                    && desc.lapJoint==native.lapJoint,"splitter geometry and stacking match the vanilla model="+desc.modelIndex);
            }
        });
        Test("prototypes", () => {
            var original = LDB.items.Select(2313); var item = LDB.items.Select(Prototypes.ItemId); var recipe = LDB.recipes.Select(Prototypes.RecipeId);
            Check(item.name == "喷涂工厂" || item.name == "Spray Factory", "localized item name");
            Check(item.iconSprite != original.iconSprite && recipe.iconSprite == item.iconSprite && item.iconSprite==LDB.models.Select(Prototypes.ModelId).iconSprite,
                "factory item, recipe and model share an independent icon");
            var iconPixels=item.iconSprite.texture.GetPixels();
            Check(iconPixels.All(p=>p.r==p.g && p.g==p.b) && iconPixels.Any(p=>p.a>0.5f && p.r>0.8f) && iconPixels.Any(p=>p.a>0.5f && p.r<0.3f),
                "white factory icon retains neutral highlights and shadows");
            var iconTarget=RenderTexture.GetTemporary(256,256,0);var previousIconTarget=RenderTexture.active;
            var iconPreview=new Texture2D(256,256,TextureFormat.ARGB32,false);
            try {
                Graphics.Blit(item.iconSprite.texture,iconTarget);RenderTexture.active=iconTarget;
                iconPreview.ReadPixels(new Rect(0,0,256,256),0,0);iconPreview.Apply();
                File.WriteAllBytes(Path.Combine(Output,"factory-icon.png"),iconPreview.EncodeToPNG());
            } finally {RenderTexture.active=previousIconTarget;RenderTexture.ReleaseTemporary(iconTarget);UnityEngine.Object.DestroyImmediate(iconPreview);}
            Check(item.ModelIndex != original.ModelIndex && !ReferenceEquals(item.prefabDesc, original.prefabDesc), "independent model descriptor");
            Check(original.prefabDesc.isPowerConsumer && item.prefabDesc.isPowerConsumer && item.prefabDesc.workEnergyPerTick*60==300000
                && item.prefabDesc.idleEnergyPerTick==167,"factory descriptor supplies 300 kW working power and approximately 10 kW idle power");
            Check(recipe.Items.SequenceEqual(new[] {1101,1201,1301}) && recipe.ItemCounts.SequenceEqual(new[] {4,2,2})
                && recipe.Handcraft && recipe.TimeSpend==180,"factory construction uses iron, gears and circuits in three seconds");
            var beltRecipe=LDB.recipes.dataArray.First(r=>r.Results.Contains(2001));
            Check(recipe.preTech==beltRecipe.preTech && item.PreTechOverride==beltRecipe.preTech.ID
                && beltRecipe.preTech.UnlockRecipes.Contains(recipe.ID),"factory unlock matches Conveyor Belt Mk.I technology");
            Check(item.prefabDesc.portPoses.SequenceEqual(original.prefabDesc.portPoses)
                && item.prefabDesc.addonAreaPoses.SequenceEqual(original.prefabDesc.addonAreaPoses)
                && item.prefabDesc.addonAreaColPoses.SequenceEqual(original.prefabDesc.addonAreaColPoses)
                && item.prefabDesc.addonAreaSize.SequenceEqual(original.prefabDesc.addonAreaSize), "native continuous lower belt and upper feeder geometry");
            var buildMenu = UIRoot.instance.uiGame.buildMenu;
            int category = item.BuildIndex / 100, position = item.BuildIndex % 100;
            var buildProtos = (ItemProto[,])AccessTools.Field(typeof(UIBuildMenu),"protos").GetValue(null);
            Check(position < buildMenu.childButtons.Length && buildMenu.childButtons[position] != null && buildProtos[category,position]?.ID == item.ID,
                "factory build slot maps to an existing menu button");
            Check(!LDB.items.dataArray.Any(x => x.ID != item.ID && x.BuildIndex == item.BuildIndex), "factory build slot has no competing item");
            results.Add("INFO category 3: " + string.Join(", ",LDB.items.dataArray.Where(x => x.BuildIndex / 100 == 3).OrderBy(x => x.BuildIndex).Select(x => x.BuildIndex + "=" + x.name)));
            results.Add("INFO selected category: " + string.Join(", ",LDB.items.dataArray.Where(x => x.BuildIndex / 100 == category).OrderBy(x => x.BuildIndex).Select(x => x.BuildIndex + "=" + x.name)));
            Check(item.prefabDesc.signHeight > 0 && item.prefabDesc.signSize > 0,"recipe sign has visible geometry");
            Check(!ReferenceEquals(item.prefabDesc.lodMaterials[0][0], original.prefabDesc.lodMaterials[0][0]), "material isolation");
            foreach (var row in item.prefabDesc.lodMaterials)
            if (row != null)
            foreach (var mat in row)
                if (mat != null && mat.HasProperty("_EmissionMultiplier")) {
                    Check(mat.GetFloat("_EmissionMultiplier")<=0.25f,"factory body emission stays below HDR brightness " + mat.name);
                }
            var consumers=new PowerConsumerComponent[3];
            consumers[1]=new PowerConsumerComponent {id=1,workEnergyPerTick=5000,idleEnergyPerTick=167};
            consumers[2]=new PowerConsumerComponent {id=2,workEnergyPerTick=12345,idleEnergyPerTick=123};
            var component = new SpraycoaterComponent { incCapacity = Prototypes.Marker,pcId=1 };
            FactoryRuntime.SetPower(ref component,consumers);
            Check(consumers[1].requiredEnergy==167,"idle factories request native standby energy");
            component.incUsed=true;FactoryRuntime.SetPower(ref component,consumers);
            Check(consumers[1].requiredEnergy==5000,"working factories request native production energy");
            component=new SpraycoaterComponent {pcId=2,cargoBeltId=1,cargoBeltItemId=1101};
            FactoryRuntime.SetPower(ref component,consumers);
            Check(consumers[2].requiredEnergy==12345,"vanilla spray coater retains its native power demand rule");
            results.Add("INFO model=" + item.ModelIndex + " build=" + item.BuildIndex + " grid=" + item.GridIndex + " lower=" + item.prefabDesc.addonAreaPoses[0].position + " upper=" + item.prefabDesc.addonAreaPoses[1].position);
        });
        Test("factory effect rendering", () => {
            var manager=new GPUInstancingManager(); manager.Init();
            try {
                var original=LDB.items.Select(2313); var item=LDB.items.Select(Prototypes.ItemId);
                var native=manager.GetObjectRenderer(original.ModelIndex);
                var factory=manager.GetObjectRenderer(item.ModelIndex);
                int effects=0;
                for(int lod=0;lod<original.prefabDesc.lodCount;lod++) {
                    int visible=0;
                    for(int slot=0;slot<original.prefabDesc.lodMaterials[lod].Length;slot++) {
                        var material=original.prefabDesc.lodMaterials[lod][slot];
                        if(material==null) continue;
                        bool effect=material.name.IndexOf("effect",StringComparison.OrdinalIgnoreCase)>=0;
                        if(effect) {
                            effects++;
                            Check(factory.lodBatches[lod].materials[slot]==null && item.prefabDesc.lodBlueprintMaterials[lod][slot]==null,"factory GPU and blueprint batches omit spray effect lod="+lod);
                            Check(native.lodBatches[lod].materials[slot]!=null,"vanilla spray effect remains enabled lod="+lod);
                        } else if(material.name.IndexOf("water",StringComparison.OrdinalIgnoreCase)>=0) {
                            Check(factory.lodBatches[lod].materials[slot]==null,"native liquid slot is reserved for per-recipe color batches");
                        } else {
                            Check(factory.lodBatches[lod].materials[slot]!=null,"factory retains body and tank rendering lod="+lod+" slot="+slot);
                            visible++;
                        }
                    }
                    Check(visible>0,"factory model remains visible at lod="+lod);
                }
                Check(effects>0,"spray effect submeshes exist in the native model");
            } finally {manager.Free();}
        });
        Test("recipe glow", () => {
            var type=typeof(Plugin).Assembly.GetType("SprayFactory.RecipeColors");
            var recipeColor=AccessTools.Method(type,"ForRecipe");

            var products=new[]{6001,6002,6003,6004,1301,1104};
            var recipes=products.Select(item=>LDB.recipes.dataArray.First(r=>r.Results[0]==item).ID).ToArray();
            var colors=recipes.Select(id=>(Color)recipeColor.Invoke(null,new object[]{id})).ToArray();
            for(int i=0;i<colors.Length;i++) {
                results.Add("INFO glow product="+products[i]+" color="+colors[i]);
                Check(colors[i].maxColorComponent<=0.351f && colors[i].a<=0.22f,"recipe glow stays below HDR brightness and opacity limits product="+products[i]);
            }
            Check(colors[0].b>colors[0].r && colors[1].r>colors[1].b,"blue and red matrix recipes retain their dominant icon hues");
            Check(colors[4].g>colors[4].r && colors[5].r>colors[5].b,"circuit and copper recipes produce green and warm glow hues");
            Check((Color)recipeColor.Invoke(null,new object[]{recipes[0]})==colors[0],"recipe color sampling is cached");
            var emptyTexture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            emptyTexture.SetPixels(new Color[4]);emptyTexture.Apply();
            var emptySprite=Sprite.Create(emptyTexture,new Rect(0,0,2,2),Vector2.one*0.5f);
            try {
                var empty=(Color)AccessTools.Method(type,"Sample").Invoke(null,new object[]{emptySprite});
                Check(!float.IsNaN(empty.r) && empty.r==empty.g && empty.g==empty.b && empty.maxColorComponent<=0.35f,"empty icon uses a finite neutral glow color");
            } finally {UnityEngine.Object.DestroyImmediate(emptySprite);UnityEngine.Object.DestroyImmediate(emptyTexture);}
        });
        Test("native recipe liquid", () => {
            var manager=new GPUInstancingManager();manager.Init();
            var cameraObject=new GameObject("factory-liquid-camera",typeof(Camera));
            var target=new RenderTexture(1000,600,24);
            var animationBuffer=new ComputeBuffer(3,20);
            var previous=RenderTexture.active;
            var liquid=typeof(Plugin).Assembly.GetType("SprayFactory.FactoryEffects");
            var draw=AccessTools.Method(liquid,"Draw",new[]{typeof(PlanetFactory),typeof(Camera),typeof(ObjectRenderer),typeof(ComputeBuffer)});
            try {
                var renderer=manager.GetObjectRenderer(Prototypes.ModelId);
                var factory=new PlanetFactory {entityPool=new EntityData[3],entityAnimPool=new AnimData[3],entityConnPool=new int[48]};
                AccessTools.PropertySetter(typeof(PlanetFactory),"planet").Invoke(factory,new object[]{new PlanetData {id=101,radius=200}});
                factory.planet.factory=factory;manager.specifyPlanet=factory.planet;
                factory.cargoTraffic=new CargoTraffic {factory=factory,spraycoaterCursor=3,spraycoaterPool=new SpraycoaterComponent[3]};
                var camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=2.4f;
                camera.transform.position=new Vector3(0,204,7);camera.transform.LookAt(new Vector3(0,201.5f,0));
                camera.backgroundColor=new Color(0.04f,0.05f,0.07f);camera.clearFlags=CameraClearFlags.SolidColor;camera.targetTexture=target;
                camera.depthTextureMode=DepthTextureMode.Depth;
                var recipes=new[]{LDB.recipes.dataArray.First(r=>r.Results[0]==6001).ID,LDB.recipes.dataArray.First(r=>r.Results[0]==6002).ID};
                FactoryStore.Clear();
                var ids=new uint[2];
                for(int id=1;id<=2;id++) {
                    var pos=new Vector3(id==1?-1.5f:1.5f,200.2f,0);
                    int modelId=renderer.AddInst(id,pos,Quaternion.identity);
                    ids[id-1]=(uint)modelId;
                    factory.entityPool[id]=new EntityData {id=id,protoId=(short)Prototypes.ItemId,modelIndex=(short)Prototypes.ModelId,modelId=modelId,pos=pos,rot=Quaternion.identity};
                    factory.cargoTraffic.spraycoaterPool[id]=new SpraycoaterComponent {id=id,entityId=id};
                    var state=FactoryStore.Get(101,id);state.RecipeId=recipes[id-1];
                    factory.entityAnimPool[id].working_length=-1.265f;
                    FactoryRuntime.UpdateAnimation(ref factory.entityAnimPool[id],state,LDB.recipes.Select(state.RecipeId));
                    Check(factory.entityAnimPool[id].time>=0.35f && factory.entityAnimPool[id].state==41,"selected recipe keeps liquid visible while the factory is idle");
                }
                animationBuffer.SetData(factory.entityAnimPool);
                var batch=renderer.lodBatches[0];batch.idBuffer.SetData(ids);batch.idBuffer.SetCounterValue(2);
                if(renderer is LabRenderer) batch.SetAdditionalBuffer("_StateBuffer",(ComputeBuffer)AccessTools.Field(typeof(LabRenderer),"stateBuffer").GetValue(renderer));
                batch.SetAdditionalBuffer("_AnimBuffer",animationBuffer);batch.Render();
                Check((int)draw.Invoke(null,new object[]{factory,camera,renderer,animationBuffer})==2,"native liquid batches render two independently colored tanks");
                var groups=(System.Collections.IDictionary)AccessTools.Field(liquid,"groups").GetValue(null);
                Func<int> sprayInstances=()=>{
                    int total=0;
                    foreach(var group in groups.Values) {
                        var spray=AccessTools.Field(group.GetType(),"Spray").GetValue(group);
                        total+=((List<uint>)AccessTools.Field(spray.GetType(),"Ids").GetValue(spray)).Count;
                    }
                    return total;
                };
                Check(sprayInstances()==0,"idle factories do not render the spray effect");
                foreach(var group in groups.Values) {
                    var spray=AccessTools.Field(group.GetType(),"Spray").GetValue(group);
                    var material=(Material)AccessTools.Field(spray.GetType(),"Material").GetValue(spray);
                    material.SetTexture("_CameraDepthTexture",SystemInfo.usesReversedZBuffer?Texture2D.blackTexture:Texture2D.whiteTexture);
                }
                camera.Render();RenderTexture.active=target;
                var texture=new Texture2D(1000,600,TextureFormat.RGB24,false);
                try {
                    texture.ReadPixels(new Rect(0,0,1000,600),0,0);texture.Apply();
                    File.WriteAllBytes(Path.Combine(Output,"factory-liquid.png"),texture.EncodeToPNG());
                    int blue=0,red=0;
                    foreach(var pixel in texture.GetPixels()) {
                        if(pixel.b>pixel.r*1.4f && pixel.b>0.08f) blue++;
                        if(pixel.r>pixel.b*1.4f && pixel.r>0.08f) red++;
                    }
                    Check(red>20 && blue>20,"full factory render contains visible red and blue tank liquid");
                    var idlePixels=texture.GetPixels();
                    for(int id=1;id<=2;id++) {
                        var state=FactoryStore.Get(101,id);state.SprayTicks=45;state.SprayItemId=LDB.recipes.Select(state.RecipeId).Results[0];
                        FactoryRuntime.UpdateAnimation(ref factory.entityAnimPool[id],state,LDB.recipes.Select(state.RecipeId));
                    }
                    animationBuffer.SetData(factory.entityAnimPool);batch.Render();
                    draw.Invoke(null,new object[]{factory,camera,renderer,animationBuffer});
                    Check(sprayInstances()==2,"output pulses select both native spray instances");
                    camera.Render();RenderTexture.active=target;
                    texture.ReadPixels(new Rect(0,0,1000,600),0,0);texture.Apply();
                    File.WriteAllBytes(Path.Combine(Output,"factory-spray.png"),texture.EncodeToPNG());
                    var sprayedPixels=texture.GetPixels();int changed=0;
                    for(int id=1;id<=2;id++) {
                        var point=camera.WorldToScreenPoint(factory.entityPool[id].pos+new Vector3(0,0.6f,0.75f));
                        for(int y=Math.Max(0,(int)point.y-55);y<Math.Min(600,(int)point.y+55);y++)
                            for(int x=Math.Max(0,(int)point.x-55);x<Math.Min(1000,(int)point.x+55);x++) {
                                int index=y*1000+x;var delta=sprayedPixels[index]-idlePixels[index];
                                if(delta.r+delta.g+delta.b>0.01f) changed++;
                            }
                    }
                    results.Add("INFO native spray changed pixels="+changed);
                    Check(changed>10,"native spray pulse produces a visible short burst beneath the tanks");
                    foreach(var group in groups.Values) {
                        var spray=AccessTools.Field(group.GetType(),"Spray").GetValue(group);
                        var material=(Material)AccessTools.Field(spray.GetType(),"Material").GetValue(spray);
                        Check(material.GetFloat("_SprayBrightness")<=0.5f && material.GetFloat("_SprayBrightnessBg")<=0.05f,"native spray material limits foreground and background brightness");
                    }
                } finally {UnityEngine.Object.DestroyImmediate(texture);}
                var state6=new FactoryState {RecipeId=6};var recipe=LDB.recipes.Select(6);
                FactoryState.Buffer(state6.Input,1102).Add(2,0);FactoryState.Buffer(state6.Input,1104).Add(1,0);
                var anim=new AnimData {working_length=-1.265f,prepare_length=7};
                FactoryRuntime.UpdateAnimation(ref anim,state6,recipe);float before=anim.time;
                state6.Tick(recipe,null,null);FactoryRuntime.UpdateAnimation(ref anim,state6,recipe);
                Check(Mathf.Abs(anim.time-before)<0.0001f && anim.time>0.35f,"moving ingredients into a processing batch preserves liquid level");
                Check(anim.working_length==-1.265f && anim.prepare_length==7,"liquid animation preserves native belt reshape data");
                state6.ChangeRecipe(0);FactoryRuntime.UpdateAnimation(ref anim,state6,null);
                Check(anim.time==0 && anim.state==1,"clearing a recipe empties the tank without spray pulses");
            } finally {
                AccessTools.Method(liquid,"Clear").Invoke(null,null);FactoryStore.Clear();RenderTexture.active=previous;
                animationBuffer.Release();manager.Free();UnityEngine.Object.DestroyImmediate(cameraObject);target.Release();UnityEngine.Object.DestroyImmediate(target);
            }
        });
        Test("native belt path previews", () => {
            var controllerObject = new GameObject("belt-path-controller");
            try {
                var controller = controllerObject.AddComponent<PlayerController>();
                controller.cmd = new CommandState();
                var planet = new PlanetData {id=101,radius=200};
                var factory = new PlanetFactory {entityCursor=501,entityPool=new EntityData[3],entityConnPool=new int[48],prebuildPool=new PrebuildData[2],prebuildConnPool=new int[32]};
                var tool = new BuildTool_Path {factory=factory,planet=planet,cursorValid=true,pathPoints=new Vector3[256],pathAlternative=1};
                var action = new PlayerAction_Build {planetAux=new PlanetAuxData(planet), model=new BuildModel()};
                AccessTools.PropertySetter(typeof(BuildTool),"controller").Invoke(tool,new object[]{controller});
                AccessTools.PropertySetter(typeof(BuildTool),"actionBuild").Invoke(tool,new object[]{action});
                AccessTools.PropertySetter(typeof(BuildTool),"buildPreviews").Invoke(tool,new object[]{new List<BuildPreview>()});
                AccessTools.Field(typeof(BuildTool_Path),"uiGame").SetValue(tool,UIRoot.instance.uiGame);
                tool.handItem=LDB.items.Select(2001); tool.handPrefabDesc=tool.handItem.prefabDesc;
                var pos=new Vector3(0,0,200.2f);
                foreach (int yaw in new[]{0,90,180,270}) {
                    var rot=Quaternion.FromToRotation(Vector3.up,Vector3.forward)*Quaternion.Euler(0,yaw,0);
                    factory.entityPool[1]=new EntityData {id=1,protoId=(short)Prototypes.ItemId,modelIndex=(short)Prototypes.ModelId,pos=pos,rot=rot};
                    factory.entityPool[2]=new EntityData {id=2,protoId=2313,modelIndex=(short)LDB.items.Select(2313).ModelIndex,pos=pos,rot=rot};
                    factory.prebuildPool[1]=new PrebuildData {id=1,protoId=(short)Prototypes.ItemId,modelIndex=(short)Prototypes.ModelId,pos=pos,rot=rot};
                    foreach (int level in new[]{0,1}) foreach (bool outgoing in new[]{false,true}) foreach (int side in new[]{-1,1}) {
                        Vector3[] nativePath=null;
                        foreach(int id in new[]{2,1,-1}) {
                            controller.cmd.stage=0; tool.altitude=level; tool.DeterminePreviews();
                            controller.cmd.stage=1;
                            tool.startObjectId=outgoing?id:0; tool.castObjectId=outgoing?0:id;
                            tool.startNearestAddonAreaIdx=level;
                            var addon=tool.GetPrefabDesc(id).addonAreaPoses[level];
                            var center=pos+rot*addon.position;
                            tool.castGroundPosSnapped=center.normalized*200.2f;
                            var outside=(center+rot*addon.forward*side*12).normalized*center.magnitude;
                            tool.startTarget=outgoing?pos:outside; tool.cursorTarget=outgoing?outside:pos;
                            tool.DeterminePreviews();
                            var endpoint=outgoing?tool.buildPreviews.First():tool.buildPreviews.Last();
                            Check(tool.ObjectIsAddonBuilding(id) && endpoint.addonObjId==id && endpoint.addonAreaIdx==level,
                                "native addon attaches level="+level+" yaw="+yaw+" object="+id+" outgoing="+outgoing+" side="+side);
                            Check(tool.buildPreviews.Count>2 && tool.buildPreviews.All(p=>p.condition==EBuildCondition.Ok),"native addon path is connectable");
                            var positions=tool.buildPreviews.Select(p=>p.lpos).ToArray();
                            if(id==2) nativePath=positions;
                            else Check(positions.SequenceEqual(nativePath),"factory belt path matches vanilla spray coater at level="+level);
                        }
                    }
                }
            } finally {UnityEngine.Object.DestroyImmediate(controllerObject);}
        });
        Test("building settings and blueprint recipes",()=>{
            var previous=GameMain.data;
            try {
                GameMain.data=new GameData {history=new GameHistoryData {recipeUnlocked=new HashSet<int>{6,50}}};
                var planet=new PlanetData {id=101,radius=200,factoryLoaded=true};
                var factory=new PlanetFactory {entityPool=new EntityData[4],entityConnPool=new int[64],entitySignPool=new SignData[4],prebuildPool=new PrebuildData[2],prebuildConnPool=new int[32]};
                AccessTools.PropertySetter(typeof(PlanetFactory),"planet").Invoke(factory,new object[]{planet});
                AccessTools.PropertySetter(typeof(PlanetFactory),"gameData").Invoke(factory,new object[]{GameMain.data});
                planet.factory=factory; planet.aux=new PlanetAuxData(planet);
                factory.cargoTraffic=new CargoTraffic {factory=factory};
                var pos=new Vector3(0,0,200.2f); var rot=Maths.SphericalRotation(pos,90);
                for(int id=1;id<=2;id++) factory.entityPool[id]=new EntityData {id=id,protoId=(short)Prototypes.ItemId,modelIndex=(short)Prototypes.ModelId,pos=pos,rot=rot};
                factory.entityPool[3]=new EntityData {id=3,protoId=2313,modelIndex=(short)LDB.items.Select(2313).ModelIndex};
                factory.prebuildPool[1]=new PrebuildData {id=1,protoId=(short)Prototypes.ItemId,modelIndex=(short)Prototypes.ModelId,pos=pos,rot=rot};
                FactoryStore.Clear(); var source=FactoryStore.Get(101,1); source.RecipeId=6;source.ForceAccMode=true;
                FactoryState.Buffer(source.Input,1102).Add(2,8);source.Progress=12;source.Duration=60;
                var settings=new BuildingParameters();
                Check(settings.CopyFromFactoryObject(1,factory) && settings.recipeId==6,"shift-copy captures the factory recipe");
                var previews=new List<BuildPreview>();settings.GenerateBuildPreviews(previews);
                Check(previews.Count==1 && previews[0].recipeId==6 && previews[0].paramCount==4,"placement preview carries recipe and production mode");
                var fromPreview=new BuildingParameters();fromPreview.CopyFromBuildPreview(previews[0]);
                Check(fromPreview.recipeId==6 && fromPreview.mode0==1,"preview parameters decode the chosen production mode");
                Check(settings.CanPasteToFactoryObject(2,factory) && settings.PasteToFactoryObject(2,factory),"factory settings paste into a second factory");
                var target=FactoryStore.Get(101,2);
                Check(target.RecipeId==6 && target.ForceAccMode && target.Progress==0 && target.Input.Count==0,"settings copy excludes inventory and processing progress");
                Check(!settings.CanPasteToFactoryObject(3,factory),"factory settings cannot paste into a vanilla spray coater");
                Check(settings.PasteToFactoryObject(-1,factory),"settings paste into a prebuild");
                var prebuildSettings=new BuildingParameters();prebuildSettings.CopyFromFactoryObject(-1,factory);
                Check(prebuildSettings.recipeId==6 && prebuildSettings.mode0==1,"copying a prebuild retains its recipe and mode");
                target.ChangeRecipe(0);target.ForceAccMode=false;
                BuildingParameters.ApplyPrebuildParametersToEntity(2,factory.prebuildPool[1].recipeId,0,factory.prebuildPool[1].parameters,null,factory);
                Check(target.RecipeId==6 && target.ForceAccMode,"construction completion restores custom factory settings");
                var blueprint=BlueprintData.CreateNew();
                BlueprintUtils.GenerateBlueprintData(blueprint,planet,planet.aux,new[]{1},1,0,null,false);
                var saved=blueprint.Clone();
                Check(saved!=null && saved.buildings.Length==1 && saved.buildings[0].recipeId==6,"native blueprint serialization retains the selected recipe");
                target.ChangeRecipe(0);target.ForceAccMode=false;
                var building=saved.buildings[0];
                BuildingParameters.ApplyPrebuildParametersToEntity(2,building.recipeId,0,building.parameters,null,factory);
                Check(target.RecipeId==6 && target.ForceAccMode,"a serialized blueprint restores recipe and production mode");
            } finally {GameMain.data=previous;FactoryStore.Clear();}
        });
        Test("addon placement settings", () => {
            var previousData=GameMain.data; var previousTemplate=BuildingParameters.template;
            var controllerObject=new GameObject("addon-placement-controller");
            var package=new StorageComponent(40);
            PlanetFactory factory=null;
            try {
                var player=new Player();
                AccessTools.PropertySetter(typeof(Player),"package").Invoke(player,new object[]{package});
                var data=new GameData {history=new GameHistoryData {recipeUnlocked=new HashSet<int>{6,50}},preferences=new GamePrefsData()};
                AccessTools.PropertySetter(typeof(GameData),"mainPlayer").Invoke(data,new object[]{player});
                GameMain.data=data;
                var planet=new PlanetData {id=101,radius=200};
                factory=new PlanetFactory {entityPool=new EntityData[3],entitySignPool=new SignData[3],prebuildPool=new PrebuildData[32],prebuildConnPool=new int[512],prebuildCursor=1,hashSystemStatic=new HashSystem()};
                AccessTools.Field(typeof(PlanetFactory),"prebuildCapacity").SetValue(factory,32);
                AccessTools.PropertySetter(typeof(PlanetFactory),"planet").Invoke(factory,new object[]{planet});
                AccessTools.PropertySetter(typeof(PlanetFactory),"gameData").Invoke(factory,new object[]{data});
                planet.factory=factory; planet.aux=new PlanetAuxData(planet);
                factory.constructionSystem=new ConstructionSystem(planet);
                var pos=new Vector3(0,0,200.2f); var rot=Maths.SphericalRotation(pos,90);
                for(int id=1;id<=2;id++) factory.entityPool[id]=new EntityData {id=id,protoId=(short)Prototypes.ItemId,modelIndex=(short)Prototypes.ModelId,pos=pos,rot=rot};
                FactoryStore.Clear(); var source=FactoryStore.Get(101,1); source.RecipeId=6;source.ForceAccMode=true;
                var controller=controllerObject.AddComponent<PlayerController>(); controller.cmd=new CommandState();
                var action=new PlayerAction_Build {templatePreviews=new List<BuildPreview>()};
                var tool=new BuildTool_Addon {factory=factory,planet=planet,handItem=LDB.items.Select(Prototypes.ItemId),handPrefabDesc=LDB.items.Select(Prototypes.ItemId).prefabDesc,cursorValid=true,cursorTarget=pos};
                AccessTools.PropertySetter(typeof(BuildTool),"controller").Invoke(tool,new object[]{controller});
                AccessTools.PropertySetter(typeof(BuildTool),"actionBuild").Invoke(tool,new object[]{action});
                AccessTools.PropertySetter(typeof(BuildTool),"player").Invoke(tool,new object[]{player});
                AccessTools.PropertySetter(typeof(BuildTool),"buildPreviews").Invoke(tool,new object[]{new List<BuildPreview>()});
                AccessTools.Method(typeof(BuildTool_Addon),"_OnInit").Invoke(tool,null);
                package.grids[0]=new StorageComponent.GRID {itemId=Prototypes.ItemId,count=8,stackSize=100};
                var recipes=new[]{6,6,50,0}; var modes=new[]{true,true,false,true};
                for(int i=0;i<recipes.Length;i++) {
                    source.ChangeRecipe(recipes[i]); source.ForceAccMode=modes[i];
                    BuildingParameters.template.CopyFromFactoryObject(1,factory); action.NotifyTemplateChange();
                    tool.DeterminePreviews();
                    Check(tool.buildPreviews.Count==1 && tool.buildPreviews[0].recipeId==recipes[i],"native addon placement preview retains shift-copied recipe iteration="+i);
                    tool.CreatePrebuilds();
                    var placed=factory.prebuildPool[-tool.buildPreviews[0].objId];
                    Check(placed.recipeId==recipes[i] && placed.paramCount==4 && placed.parameters[3]==(modes[i]?1:0),"native addon placement stores recipe and mode in the prebuild");
                    var target=FactoryStore.Get(101,2); target.ChangeRecipe(0); target.ForceAccMode=!modes[i];
                    BuildingParameters.ApplyPrebuildParametersToEntity(2,placed.recipeId,placed.filterId,placed.parameters,placed.content,factory);
                    Check(target.RecipeId==recipes[i] && target.ForceAccMode==modes[i],"placed addon parameters restore factory settings at construction");
                }
                BuildingParameters.template.SetEmpty(); action.NotifyTemplateChange(); tool.DeterminePreviews();
                Check(tool.buildPreviews[0].recipeId==0 && tool.buildPreviews[0].parameters[0]!=Prototypes.Marker,"ordinary addon placement has no copied factory recipe");
                BuildingParameters.template.CopyFromFactoryObject(1,factory); action.NotifyTemplateChange();
                tool.handItem=LDB.items.Select(2313); tool.handPrefabDesc=tool.handItem.prefabDesc; tool.DeterminePreviews();
                Check(tool.buildPreviews[0].recipeId==0 && tool.buildPreviews[0].paramCount==128 && tool.buildPreviews[0].parameters[0]!=Prototypes.Marker,"vanilla spray coater placement keeps its native parameters");
            } finally {
                factory?.constructionSystem?.Free(); factory?.hashSystemStatic?.Free();
                GameMain.data=previousData; BuildingParameters.template=previousTemplate; FactoryStore.Clear();
                package.Free(); UnityEngine.Object.DestroyImmediate(controllerObject);
            }
        });
        Test("recipe execution", () => {
            foreach (int id in new[] {1,6,30,50,66,75,137,16})
            {
                var recipe = LDB.recipes.Select(id); var state = new FactoryState { RecipeId = id };
                for (int i=0;i<recipe.Items.Length;i++) FactoryState.Buffer(state.Input,recipe.Items[i]).Add(recipe.ItemCounts[i],0);
                int produced=0,consumed=0;
                for(int i=0;i<recipe.TimeSpend;i++) state.Tick(recipe,(item,n)=>consumed+=n,(item,n)=>produced+=n);
                Check(produced == recipe.ResultCounts.Sum() && consumed == recipe.ItemCounts.Sum() && state.Progress == 0,"exact counts, time and multiple outputs recipe="+id);
            }
        });
        Test("cancel and serialization", () => {
            FactoryStore.Clear(); var recipe=LDB.recipes.Select(6); var state=FactoryStore.Get(101,7); state.RecipeId=6;
            FactoryState.Buffer(state.Input,1102).Add(2,8); FactoryState.Buffer(state.Input,1104).Add(1,4);
            state.Tick(recipe,null,null);
            state.ForceAccMode=true;
            using(var stream=new MemoryStream())
            {
                FactoryStore.Export(new BinaryWriter(stream)); stream.Position=0; FactoryStore.Import(new BinaryReader(stream));
            }
            state=FactoryStore.Get(101,7);
            Check(state.Progress==1 && state.Pending[1102].Count==2 && state.Pending[1102].Inc==8,"in-flight save roundtrip");
            Check(state.ForceAccMode && !state.BatchAccMode,"save keeps chosen mode and current batch mode separately");
            state.ChangeRecipe(50);
            Check(state.Input[1102].Count==2 && state.Input[1102].Inc==8 && state.Pending.Count==0 && state.Duration==0,"recipe change refunds in-flight ingredients and points");
            FactoryStore.Clear(); Check(!FactoryStore.TryGet(101,7,out _),"save isolation");
        });
        Test("power-dependent production", () => {
            var recipe=LDB.recipes.Select(6);var state=new FactoryState {RecipeId=6};
            FactoryState.Buffer(state.Input,1102).Add(2,0);FactoryState.Buffer(state.Input,1104).Add(1,0);
            state.Tick(recipe,null,null,0);
            Check(state.Pending.Count==0 && state.Input[1102].Count==2 && state.Progress==0,"no power leaves ingredients and progress untouched");
            for(int i=0;i<recipe.TimeSpend;i++) state.Tick(recipe,null,null,0.5f);
            Check(state.Progress==recipe.TimeSpend/2 && state.Pending[1102].Count==2 && state.Output[1202].Count==0,
                "half power advances half of the normal production cycle");
            for(int i=0;i<60;i++) state.Tick(recipe,null,null,0);
            Check(state.Progress==recipe.TimeSpend/2 && state.Pending[1102].Count==2,"power loss preserves the in-flight production batch");
            for(int i=0;i<recipe.TimeSpend/2;i++) state.Tick(recipe,null,null,1);
            Check(state.Output[1202].Count==2 && state.Pending.Count==0 && state.Progress==0,"restored power completes the batch without loss or duplication");
            state=new FactoryState {RecipeId=6};
            FactoryState.Buffer(state.Input,1102).Add(2,0);FactoryState.Buffer(state.Input,1104).Add(1,0);
            FactoryState.Buffer(state.Output,1202).Add(FactoryState.OutputLimit(2),0);
            Check(!state.CanWork(recipe) && state.Status=="产物缓存已满","full output buffers request standby power");
        });
        Test("bonus and backpressure", () => {
            var recipe=LDB.recipes.Select(6); var state=new FactoryState {RecipeId=6};
            FactoryState.Buffer(state.Input,1102).Add(8,32); FactoryState.Buffer(state.Input,1104).Add(4,16);
            for(int i=0;i<recipe.TimeSpend*4;i++) state.Tick(recipe,null,null);
            Check(state.Output[1202].Count==10,"four sprayed batches produce one extra batch");
            state=new FactoryState {RecipeId=6}; FactoryState.Buffer(state.Input,1102).Add(2,0); FactoryState.Buffer(state.Input,1104).Add(1,0);
            FactoryState.Buffer(state.Output,1202).Add(FactoryState.OutputLimit(2),0); state.Tick(recipe,null,null);
            Check(state.Input[1102].Count==2 && state.Pending.Count==0,"full output prevents consuming inputs");
        });
        Test("production mode selection", () => {
            var recipe=LDB.recipes.Select(6); var state=new FactoryState {RecipeId=6};
            FactoryState.Buffer(state.Input,1102).Add(4,16); FactoryState.Buffer(state.Input,1104).Add(2,8);
            state.Tick(recipe,null,null);
            Check(state.Duration==60 && !state.BatchAccMode && state.BatchBonus>0,"default mode uses extra products");
            state.ForceAccMode=true;
            Check(state.Duration==60 && !state.BatchAccMode,"mode change preserves in-flight batch timing");
            for(int i=1;i<60;i++) state.Tick(recipe,null,null);
            state.Tick(recipe,null,null);
            Check(state.Duration==30 && state.BatchAccMode && state.BatchBonus==0,"forced acceleration applies on the next batch");
            for(int i=1;i<30;i++) state.Tick(recipe,null,null);
            Check(state.Output[1202].Count==4 && state.ExtraProgress>0 && state.ExtraProgress<1,"acceleration adds no extra products and preserves earned fractional bonus");
            var nonproductive=LDB.recipes.Select(Prototypes.RecipeId);
            state=new FactoryState {RecipeId=nonproductive.ID};
            for(int i=0;i<nonproductive.Items.Length;i++) FactoryState.Buffer(state.Input,nonproductive.Items[i]).Add(nonproductive.ItemCounts[i],nonproductive.ItemCounts[i]*4);
            state.Tick(nonproductive,null,null);
            Check(!nonproductive.productive && state.BatchAccMode && state.Duration==nonproductive.TimeSpend/2 && state.BatchBonus==0,"nonproductive recipe uses acceleration with default preference");
        });
        Test("legacy save compatibility", () => {
            using(var stream=new MemoryStream())
            {
                var writer=new BinaryWriter(stream); writer.Write(1); writer.Write(2);
                foreach(int entity in new[]{1,2})
                {
                    writer.Write(FactoryStore.Key(101,entity)); writer.Write(6); writer.Write(0); writer.Write(0);
                    writer.Write(0d); writer.Write(0d); writer.Write(0);
                    writer.Write(0); writer.Write(0); writer.Write(0);
                }
                stream.Position=0; FactoryStore.Import(new BinaryReader(stream));
                Check(!FactoryStore.Get(101,1).ForceAccMode && FactoryStore.Get(101,2).RecipeId==6,"version one multi-factory save loads with default mode");
            }
            FactoryStore.Clear();
        });
        Test("belt pickup and stacking", () => {
            var container = new CargoContainer();
            try {
                var path = new CargoPath(container) {id=1}; AccessTools.Field(typeof(CargoPath),"bufferLength").SetValue(path,100);
                var traffic=new CargoTraffic {container=container,pathPool=new[]{(CargoPath)null,path},beltPool=new BeltComponent[2]};
                traffic.beltPool[1]=new BeltComponent {id=1,segPathId=1,segIndex=40,segPivotOffset=5};
                var recipe=LDB.recipes.Select(6); var state=new FactoryState {RecipeId=6};
                Check(path.TryInsertItemAtHead(1104,4,16,40),"upper material cargo insertion");
                FactoryRuntime.Receive(traffic,1,state,recipe);
                Check(state.Input.ContainsKey(1104) && state.Input[1104].Count==2 && state.Input[1104].Inc==8,"upper pickup caps ingredients at two recipe rounds");
                int remainder = path.GetCargoIdAtIndex(43,5);
                Check(remainder>=0 && container.cargoPool[remainder].stack==2 && container.cargoPool[remainder].inc==8,"partial stack pickup preserves remaining count and points");
                FactoryRuntime.Receive(traffic,1,state,recipe);
                Check(state.Input[1104].Count==2 && container.cargoPool[remainder].stack==2,"full input buffer stops collection");
                state.Input[1104].Add(6,24);
                FactoryRuntime.Receive(traffic,1,state,recipe);
                Check(state.Input[1104].Count==8 && state.Input[1104].Inc==32 && container.cargoPool[remainder].stack==2
                    && container.cargoPool[remainder].inc==8,"manual stock above two batches stops belt pickup without removing stock or cargo");
                state.Input[1104].Take(7);
                FactoryRuntime.Receive(traffic,1,state,recipe);
                Check(state.Input[1104].Count==2 && state.Input[1104].Inc==8 && container.cargoPool[remainder].stack==1
                    && container.cargoPool[remainder].inc==4,"belt pickup resumes below two batches and fills only the automatic target");
                path.TryPickItem(40,10,out _,out _);
                var old=GameMain.data; var history=new GameHistoryData(); GameMain.data=new GameData {history=history};
                try {
                    foreach(int level in new[]{1,2,3,4,8,255}) {
                        state.SprayTicks=0;
                        history.stationPilerLevel=level; FactoryState.Buffer(state.Output,1202).Add(level-1,0); FactoryRuntime.Send(traffic,1,state);
                        Check(path.GetCargoIdAtIndex(43,5)<0 && state.Output[1202].Count==level-1,"output waits for a full technology stack "+level);
                        Check(state.SprayTicks==0,"accumulating a partial output stack does not spray");
                        FactoryState.Buffer(state.Output,1202).Add(1,0); FactoryRuntime.Send(traffic,1,state);
                        int cargo=path.GetCargoIdAtIndex(43,5);
                        Check(cargo>=0 && container.cargoPool[cargo].stack==level && state.Output[1202].Count==0,"output respects stack technology "+level);
                        Check(state.SprayTicks==45 && state.SprayItemId==1202,"successful output starts a spray pulse for the exported item");
                        path.TryPickItem(40,10,out _,out _);
                    }
                    history.stationPilerLevel=4; FactoryState.Buffer(state.Output,1201).Add(1,0); FactoryState.Buffer(state.Output,1202).Add(4,0);
                    FactoryRuntime.Send(traffic,1,state);
                    int ready=path.GetCargoIdAtIndex(43,5);
                    Check(ready>=0 && container.cargoPool[ready].item==1202 && container.cargoPool[ready].stack==4 && state.Output[1201].Count==1,"partial secondary product does not block a full product stack");
                    path.TryPickItem(40,10,out _,out _);
                    history.stationPilerLevel=2; FactoryState.Buffer(state.Output,1201).Add(1,0); FactoryRuntime.Send(traffic,1,state);
                    ready=path.GetCargoIdAtIndex(43,5);
                    Check(ready>=0 && container.cargoPool[ready].item==1201 && container.cargoPool[ready].stack==2,"output immediately follows a changed stack technology level");
                    path.TryPickItem(40,10,out _,out _);
                    history.stationPilerLevel=255; state=new FactoryState {RecipeId=6};
                    for(int batch=0;batch<128;batch++) {
                        FactoryState.Buffer(state.Input,1102).Add(2,0); FactoryState.Buffer(state.Input,1104).Add(1,0);
                        for(int tick=0;tick<recipe.TimeSpend;tick++) state.Tick(recipe,null,null);
                        FactoryRuntime.Send(traffic,1,state);
                    }
                    ready=path.GetCargoIdAtIndex(43,5);
                    Check(ready>=0 && container.cargoPool[ready].stack==255 && state.Output[1202].Count==1,"production can accumulate a full extended stack beyond the default output capacity");
                } finally {GameMain.data=old;}
                path.Free();
            } finally {container.Free();}
        });
        Test("connected factory tick", () => {
            var old=GameMain.data; var container=new CargoContainer();
            try {
                var history=new GameHistoryData {recipeUnlocked=new HashSet<int> {6},stationPilerLevel=4};
                var stat=new FactoryProductionStat {productRegister=new int[12000],consumeRegister=new int[12000]};
                GameMain.data=new GameData {history=history,statistics=new GameStatData {production=new ProductionStatistics {factoryStatPool=new[]{stat}}}};
                var planet=new PlanetData {id=101};
                var factory=new PlanetFactory {entityPool=new EntityData[5],entityConnPool=new int[80],entityAnimPool=new AnimData[5],entitySignPool=new SignData[5]};
                AccessTools.PropertySetter(typeof(PlanetFactory),"planet").Invoke(factory,new object[]{planet});
                planet.factory=factory;
                var traffic=new CargoTraffic {factory=factory,container=container,beltPool=new BeltComponent[4],pathPool=new CargoPath[4],spraycoaterPool=new SpraycoaterComponent[2],spraycoaterCursor=2};
                factory.cargoTraffic=traffic; factory.powerSystem=new PowerSystem(planet);
                var pos=new Vector3(0,0,200.2f); var rot=Maths.SphericalRotation(pos,90);
                factory.entityPool[1]=new EntityData {id=1,protoId=(short)Prototypes.ItemId,modelIndex=(short)Prototypes.ModelId,spraycoaterId=1,pos=pos,rot=rot};
                for(int i=1;i<=3;i++) {
                    traffic.pathPool[i]=new CargoPath(container) {id=i}; AccessTools.Field(typeof(CargoPath),"bufferLength").SetValue(traffic.pathPool[i],100);
                    traffic.beltPool[i]=new BeltComponent {id=i,segPathId=i,segIndex=40,segPivotOffset=5};
                }
                factory.entityPool[2]=new EntityData {id=2,beltId=1,pos=pos,rot=rot};
                factory.entityPool[3]=new EntityData {id=3,beltId=3,pos=pos+rot*LDB.items.Select(Prototypes.ItemId).prefabDesc.addonAreaPoses[1].position,rot=rot};
                traffic.beltPool[1].entityId=2; traffic.beltPool[3].entityId=3;
                traffic.spraycoaterPool[1]=new SpraycoaterComponent {id=1,entityId=1,incCapacity=Prototypes.Marker};
                var powerType=typeof(Plugin).Assembly.GetType("SprayFactory.FactoryPower");
                AccessTools.Method(powerType,"Restore").Invoke(null,new object[]{factory});
                int consumer=traffic.spraycoaterPool[1].pcId;
                Check(consumer>0 && factory.entityPool[1].powerConId==consumer && factory.powerSystem.consumerPool[consumer].entityId==1,
                    "loading a factory without a power component creates and links a native consumer");
                int consumerCursor=factory.powerSystem.consumerCursor;
                AccessTools.Method(powerType,"Restore").Invoke(null,new object[]{factory});
                Check(factory.powerSystem.consumerCursor==consumerCursor,"power restoration reuses existing consumers");
                factory.powerSystem.netPool[1]=new PowerNetwork();factory.powerSystem.netPool[1].Reset(1);
                factory.powerSystem.netPool[1].consumerRatio=1;
                factory.powerSystem.consumerPool[consumer].networkId=1;
                factory.TryConnAddonBuilding(1,2); factory.TryConnAddonBuilding(1,3);
                Check(traffic.spraycoaterPool[1].cargoBeltId==1 && traffic.spraycoaterPool[1].incBeltId==3,"native addon construction connects lower and upper belts to distinct channels");
                FactoryStore.Clear(); var state=FactoryStore.Get(101,1); state.RecipeId=6;
                traffic.pathPool[1].TryInsertItemAtHead(1102,2,0,40);
                traffic.pathPool[3].TryInsertItemAtHead(1104,1,0,40);
                factory.powerSystem.consumerPool[consumer].networkId=0;
                traffic.SpraycoaterGameTick();
                Check(state.Progress==0 && state.Pending.Count==0 && traffic.pathPool[3].GetCargoIdAtIndex(43,5)>=0
                    && factory.powerSystem.consumerPool[consumer].requiredEnergy==Prototypes.IdleEnergyPerTick,"disconnected factories leave belt inputs untouched and request standby power");
                Check(factory.entitySignPool[1].signType==SignData.NO_POWER_CONN,"disconnected factories display the native power connection warning");
                factory.powerSystem.consumerPool[consumer].networkId=1;
                traffic.SpraycoaterGameTick();
                Check(state.Input[1102].Count==0 && state.Input[1104].Count==1 && state.Progress==0,"lower belt ingredients do not supply the factory");
                int lowerCargo=traffic.pathPool[1].GetCargoIdAtIndex(43,5);
                Check(lowerCargo>=0 && container.cargoPool[lowerCargo].stack==2,"lower belt preserves passing ingredients");
                traffic.pathPool[1].TryPickItem(40,10,out _,out _);
                traffic.pathPool[3].TryInsertItemAtHead(1102,2,0,40);
                factory.powerSystem.netPool[1].consumerRatio=0.5;
                for(int i=0;i<60;i++) traffic.SpraycoaterGameTick();
                Check(state.Progress==30 && stat.productRegister[1202]==0 && factory.powerSystem.consumerPool[consumer].requiredEnergy==Prototypes.WorkEnergyPerTick,
                    "native grid satisfaction scales factory progress while requesting working power");
                factory.powerSystem.netPool[1].consumerRatio=0;
                for(int i=0;i<30;i++) traffic.SpraycoaterGameTick();
                Check(state.Progress==30 && state.Pending[1102].Count==2 && state.Status=="未供电","native grid power loss pauses the factory batch");
                Check(factory.entitySignPool[1].signType==SignData.NO_POWER,"connected factories display the native power outage warning");
                factory.powerSystem.netPool[1].consumerRatio=1;
                for(int i=0;i<30;i++) traffic.SpraycoaterGameTick();
                Check(stat.productRegister[1202]==2 && stat.consumeRegister[1102]==2 && stat.consumeRegister[1104]==1,"powered upper feeder supplies every ingredient");
                int cargo=traffic.pathPool[1].GetCargoIdAtIndex(43,5);
                Check(cargo<0 && state.Output[1202].Count==2,"completed products stay buffered until the four-layer technology stack is full");
                Check(factory.entityAnimPool[1].state==41 && factory.entityAnimPool[1].state/1000==0 && factory.entityAnimPool[1].time>=0.35f,"liquid remains visible without activating spray pulses");
                traffic.pathPool[3].TryInsertItemAtHead(1102,2,0,40); traffic.SpraycoaterGameTick();
                traffic.pathPool[3].TryInsertItemAtHead(1104,1,0,40);
                for(int i=0;i<60;i++) traffic.SpraycoaterGameTick();
                cargo=traffic.pathPool[1].GetCargoIdAtIndex(43,5);
                Check(cargo>=0 && container.cargoPool[cargo].item==1202 && container.cargoPool[cargo].stack==4 && state.Output[1202].Count==0,"two production batches export one full four-layer stack at the factory pivot");
                Check(state.SprayTicks==45 && factory.entityAnimPool[1].state==45141,"native spray animation starts when a full output stack reaches the belt");
                Check(stat.productRegister[1202]==4 && stat.consumeRegister[1102]==4 && stat.consumeRegister[1104]==2,"waiting for a full output stack preserves production and consumption accounting");
                Check(traffic.pathPool[1].GetCargoIdAtIndex(0,10)<0 && traffic.pathPool[3].GetCargoIdAtIndex(43,5)<0,"output uses the lower pivot and leaves the upper belt empty");
                Check(state.Input[1102].Count==0 && state.Input[1104].Count==0,"output is not re-ingested");
                FactoryState.Buffer(state.Output,1202).Add(4,0);
                factory.powerSystem.netPool[1].consumerRatio=0;
                traffic.pathPool[1].TryPickItem(40,10,out _,out _);traffic.SpraycoaterGameTick();
                Check(state.Output[1202].Count==4 && traffic.pathPool[1].GetCargoIdAtIndex(43,5)<0,"power loss also pauses buffered product export");
                traffic.TryInsertItem(1,0,1202,4,0);
                factory.powerSystem.netPool[1].consumerRatio=1;
                traffic.SpraycoaterGameTick();
                Check(state.Output[1202].Count==4,"occupied lower belt keeps products in the output buffer");
                Check(state.SprayTicks==43,"a blocked output belt does not restart the spray animation");
                traffic.pathPool[1].TryPickItem(40,10,out _,out _); traffic.SpraycoaterGameTick();
                cargo=traffic.pathPool[1].GetCargoIdAtIndex(43,5);
                Check(state.Output[1202].Count==0 && cargo>=0 && container.cargoPool[cargo].stack==4,"clearing the lower belt resumes full-stack output without item loss");
                for(int i=0;i<45;i++) traffic.SpraycoaterGameTick();
                Check(state.SprayTicks==0 && factory.entityAnimPool[1].state==41,"spray pulse expires while liquid remains visible");
                Check(factory.entitySignPool[1].iconId0==6 && factory.entitySignPool[1].iconType==2,"working factory displays the selected recipe sign");
                state.ChangeRecipe(50); traffic.SpraycoaterGameTick();
                Check(factory.entitySignPool[1].iconId0==50 && factory.entitySignPool[1].iconType==2,"recipe change updates the world sign");
                state.ChangeRecipe(0); traffic.SpraycoaterGameTick();
                Check(factory.entitySignPool[1].iconId0==0 && factory.entitySignPool[1].iconType==0,"clearing recipe removes the world icon");
                for(int i=1;i<=3;i++) traffic.pathPool[i].Free();
            } finally {GameMain.data=old; container.Free(); FactoryStore.Clear();}
        });
        Test("reference production statistics", () => {
            var stat=new FactoryProductionStat(); stat.Init();
            try {
                var history=new GameHistoryData {recipeUnlocked=new HashSet<int>{6}};
                var data=new GameData {history=history};
                var planet=new PlanetData {id=101};
                var factory=new PlanetFactory {entityPool=new EntityData[3]};
                AccessTools.PropertySetter(typeof(PlanetFactory),"planet").Invoke(factory,new object[]{planet});
                factory.entityPool[1]=new EntityData {id=1,protoId=(short)Prototypes.ItemId,spraycoaterId=1};
                factory.entityPool[2]=new EntityData {id=2,protoId=2313,spraycoaterId=2};
                factory.cargoTraffic=new CargoTraffic {factory=factory,spraycoaterCursor=3,spraycoaterPool=new SpraycoaterComponent[3]};
                factory.cargoTraffic.spraycoaterPool[1]=new SpraycoaterComponent {id=1,entityId=1};
                factory.cargoTraffic.spraycoaterPool[2]=new SpraycoaterComponent {id=2,entityId=2};
                FactoryStore.Clear(); var state=FactoryStore.Get(101,1); state.RecipeId=6;
                var recipe=LDB.recipes.Select(6);
                var calculator=new ProductionExtraInfoCalculator {productionStatistics=new ProductionStatistics {factoryStatPool=new[]{stat}}};
                var calculate=AccessTools.Method(typeof(ProductionExtraInfoCalculator),"CalculateFactory");
                calculate.Invoke(calculator,new object[]{data,factory});
                float baseline=3600f/recipe.TimeSpend;
                Check(Mathf.Abs(stat.productPool[stat.productIndices[1202]].refProductSpeed-baseline*2)<0.01f,"factory contributes its theoretical full-power production rate");
                Check(Mathf.Abs(stat.productPool[stat.productIndices[1102]].refConsumeSpeed-baseline*2)<0.01f,"reference consumption includes every recipe input");
                foreach(int item in recipe.Items) FactoryState.Buffer(state.Input,item).Add(4,16);
                FactoryState.Buffer(state.Output,1202).Add(7,0);
                FactoryState.Buffer(state.Pending,1102).Add(2,8); FactoryState.Buffer(state.Pending,1104).Add(1,4);
                calculate.Invoke(calculator,new object[]{data,factory});
                Check(Mathf.Abs(stat.productPool[stat.productIndices[1202]].refProductSpeed-baseline*2*(1+(float)Cargo.incTableMilli[4]))<0.01f,"expected production applies extra-product mode");
                Check(stat.productPool[stat.productIndices[1102]].storageCount==6 && stat.productPool[stat.productIndices[1202]].storageCount==7,"statistics include input, pending and output buffers");
                state.ForceAccMode=true; calculate.Invoke(calculator,new object[]{data,factory});
                float accelerated=3600f/Math.Max(1,(int)Math.Ceiling(recipe.TimeSpend/(1+Cargo.accTableMilli[4])));
                Check(Mathf.Abs(stat.productPool[stat.productIndices[1202]].refProductSpeed-accelerated*2)<0.01f
                    && Mathf.Abs(stat.productPool[stat.productIndices[1104]].refConsumeSpeed-accelerated)<0.01f,"acceleration affects production and consumption with integer tick duration");
                calculate.Invoke(calculator,new object[]{data,factory});
                Check(Mathf.Abs(stat.productPool[stat.productIndices[1202]].refProductSpeed-accelerated*2)<0.01f,"statistics refresh replaces previous reference rates");
                state.ChangeRecipe(0); calculate.Invoke(calculator,new object[]{data,factory});
                Check(stat.productPool[stat.productIndices[1202]].refProductSpeed==0,"clearing recipe removes expected production");
                state.RecipeId=6; factory.entityPool[1].id=0; calculate.Invoke(calculator,new object[]{data,factory});
                Check(stat.productPool[stat.productIndices[1202]].refProductSpeed==0,"removed factories and vanilla spray coaters contribute no custom rate");
            } finally {stat.Free();FactoryStore.Clear();}
        });
        Test("factory window", () => {
            var previousData=GameMain.data;
            var package=new StorageComponent(40);
            package.grids[0]=new StorageComponent.GRID {itemId=1101,count=20,stackSize=100};
            package.grids[1]=new StorageComponent.GRID {itemId=1102,count=12,stackSize=100};
            var player=new Player {packageColCount=10};
            AccessTools.PropertySetter(typeof(Player),"package").Invoke(player,new object[]{package});
            GameMain.data=new GameData {history=new GameHistoryData {recipeUnlocked=new HashSet<int>{6,Prototypes.RecipeId}}};
            AccessTools.PropertySetter(typeof(GameData),"mainPlayer").Invoke(GameMain.data,new object[]{player});
            var window=UIRoot.instance.uiGame.spraycoaterWindow;
            var originalWindowSize=((RectTransform)window.transform).sizeDelta;
            var originalWindowPosition=((RectTransform)window.transform).anchoredPosition;
            var factory=new PlanetFactory {entityPool=new EntityData[2]};
            AccessTools.PropertySetter(typeof(PlanetFactory),"planet").Invoke(factory,new object[]{new PlanetData {id=101}});
            factory.entityPool[1]=new EntityData {id=1,protoId=(short)Prototypes.ItemId,spraycoaterId=1};
            var traffic=new CargoTraffic {factory=factory,spraycoaterPool=new SpraycoaterComponent[2]};
            traffic.spraycoaterPool[1]=new SpraycoaterComponent {id=1,entityId=1};
            var previousFactory=window.factory; var previousTraffic=window.traffic;
            AccessTools.Field(typeof(UISpraycoaterWindow),"_spraycoaterId").SetValue(window,1);
            window.factory=factory; window.traffic=traffic;
            var type=typeof(Plugin).Assembly.GetType("SprayFactory.FactoryWindow");
            try {
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                var view=(UIAssemblerWindow)AccessTools.Field(type,"view").GetValue(null);
                var native=UIRoot.instance.uiGame.assemblerWindow;
                Check(view!=null && view.recipeGroup.anchoredPosition==native.recipeGroup.anchoredPosition && view.recipeGroup.sizeDelta==native.recipeGroup.sizeDelta,
                    "factory recipe selector retains assembler layout and scale");
                Check(view.produceGroup.anchoredPosition==native.produceGroup.anchoredPosition && view.servingGroup.anchoredPosition==native.servingGroup.anchoredPosition,
                    "production and input groups retain native positions");
                Check(view.stateText.font==native.stateText.font && view.stateText.fontSize==native.stateText.fontSize && view.stateText.alignment==native.stateText.alignment
                    && view.speedText.fontSize==native.speedText.fontSize && view.titleText.fontSize==native.titleText.fontSize,"factory typography matches native assembler window");
                var drag=view.GetComponent<UIWindowDrag>();
                Check(drag.dragTrans==window.transform && drag.dragTrigger.gameObject.activeSelf,"native drag handle moves the factory window");
                var factoryRect=(RectTransform)window.transform;
                Check(Vector3.Distance(factoryRect.TransformPoint(factoryRect.rect.center),drag.screenRect.TransformPoint(drag.screenRect.rect.center))<0.1f,
                    "first factory window opens at the center of the UI canvas");
                Check(view.powerText.text=="未供电".Translate(),"factory window reports a disconnected power supply");
                factory.planet.factory=factory;factory.powerSystem=new PowerSystem(factory.planet);
                factory.powerSystem.consumerPool[1]=new PowerConsumerComponent {id=1,entityId=1,networkId=1,requiredEnergy=5000,workEnergyPerTick=5000,idleEnergyPerTick=167};
                factory.powerSystem.netPool[1]=new PowerNetwork();factory.powerSystem.netPool[1].Reset(1);
                factory.powerSystem.netPool[1].consumerRatio=0.5;traffic.spraycoaterPool[1].pcId=1;
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                Check(view.powerText.text.Contains("150") && view.powerText.text.Contains("50") && view.powerText.color==view.powerLowColor,
                    "native factory power label shows delivered power and grid satisfaction");
                factory.powerSystem.netPool[1].consumerRatio=1;
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                Check(view.powerText.text.Contains("300") && view.powerText.text.Contains("100") && view.powerText.color==view.powerNormalColor,
                    "native factory power label shows the full 300 kW working load");
                var picker=UIRoot.instance.uiGame.recipePicker;
                var pickerParent=picker.transform.parent;int pickerSibling=picker.transform.GetSiblingIndex();
                picker.transform.SetParent(UIRoot.instance.overlayCanvas.transform,true);
                bool pickerWasInited=picker.inited;
                if(!pickerWasInited) picker._Init(null);
                Check(picker.inited && GameMain.iconSet!=null,"native recipe picker initializes with the game icon atlas");
                Check(GameMain.iconSet.itemIconIndex[Prototypes.ItemId]!=GameMain.iconSet.itemIconIndex[2313]
                    && GameMain.iconSet.itemIconIndex[Prototypes.ItemId]==GameMain.iconSet.recipeIconIndex[Prototypes.RecipeId],
                    "native item and recipe atlas entries use the independent factory icon");
                var pickerTitle=picker.transform.Find("title-text").GetComponent<UnityEngine.UI.Text>();
                var originalTitleSize=pickerTitle.rectTransform.sizeDelta;bool originalTitleRaycast=pickerTitle.raycastTarget;
                var windowRect=(RectTransform)window.transform;var originalPosition=windowRect.position;
                try {
                    var screen=drag.screenRect;
                    var topLeft=screen.TransformPoint(screen.rect.center+new Vector2(-screen.rect.width*0.25f,screen.rect.height*0.25f));
                    windowRect.position+=topLeft-windowRect.TransformPoint(new Vector3(windowRect.rect.xMin,windowRect.rect.yMax,0));
                    view.recipeButton.button.onClick.Invoke();
                    var pickerDrag=picker.GetComponent<UIWindowDrag>();
                    Check(picker.active && Vector3.Distance(picker.pickerTrans.TransformPoint(new Vector3(picker.pickerTrans.rect.xMin,picker.pickerTrans.rect.yMax,0)),topLeft)<0.1f,
                        "recipe picker opens at the factory window top-left across different pivots");
                    Check(pickerDrag.enabled && pickerDrag.dragTrans==picker.pickerTrans && pickerDrag.refTrans==picker.pickerTrans && pickerDrag.screenRect==screen
                        && pickerDrag.dragTrigger.gameObject==pickerTitle.gameObject && pickerTitle.raycastTarget && pickerTitle.rectTransform.rect.width>500,
                        "recipe picker uses the native drag component across its title bar");
                    var down=pickerDrag.dragTrigger.triggers.Single(e=>e.eventID==UnityEngine.EventSystems.EventTriggerType.PointerDown);
                    down.callback.Invoke(new UnityEngine.EventSystems.BaseEventData(null));
                    Check((bool)AccessTools.Field(typeof(UIWindowDrag),"mouseDown").GetValue(pickerDrag),"picker title dispatches pointer-down to native dragging");
                    pickerDrag.dragTrigger.triggers.Single(e=>e.eventID==UnityEngine.EventSystems.EventTriggerType.PointerUp).callback.Invoke(new UnityEngine.EventSystems.BaseEventData(null));
                    var moved=picker.pickerTrans.anchoredPosition+new Vector2(40,-20);
                    AccessTools.Field(typeof(UIWindowDrag),"moving").SetValue(pickerDrag,true);
                    AccessTools.Field(typeof(UIWindowDrag),"dragPositionWanted").SetValue(pickerDrag,moved);
                    AccessTools.Field(typeof(UIWindowDrag),"dragPositionCurrent").SetValue(pickerDrag,moved);
                    AccessTools.Method(typeof(UIWindowDrag),"Update").Invoke(pickerDrag,null);
                    Check(Vector2.Distance(picker.pickerTrans.anchoredPosition,moved)<1,"native drag update moves the recipe picker independently");
                    CaptureWindow(picker,"factory-recipe-picker.png");
                    AccessTools.Field(typeof(UIRecipePicker),"selectedProto").SetValue(picker,LDB.recipes.Select(6));
                    UIRecipePicker.Close();
                    Check(FactoryStore.Get(101,1).RecipeId==6 && !pickerDrag.enabled && pickerTitle.rectTransform.sizeDelta==originalTitleSize && pickerTitle.raycastTarget==originalTitleRaycast,
                        "recipe selection applies and restores shared picker title settings");
                    view.recipeButton.button.onClick.Invoke();
                    Check(pickerDrag.enabled && pickerDrag.dragTrigger.triggers.Count==2,"reopening the factory picker registers one native drag binding");
                    UIRecipePicker.Close();
                    UIRecipePicker.Popup(Vector2.zero,_=>{},ERecipeType.None);
                    Check(!pickerDrag.enabled && pickerTitle.rectTransform.sizeDelta==originalTitleSize,"other factories retain the shared picker's native behavior");
                    UIRecipePicker.Close();
                    FactoryStore.Get(101,1).ChangeRecipe(0);
                } finally {
                    UIRecipePicker.Close();if(!pickerWasInited) picker._Free();windowRect.position=originalPosition;
                    picker.transform.SetParent(pickerParent,true);picker.transform.SetSiblingIndex(pickerSibling);
                }
                var state=FactoryStore.Get(101,1); state.ChangeRecipe(6);
                var expansionTopLeft=drag.screenRect.TransformPoint(new Vector3(drag.screenRect.rect.xMin+40,drag.screenRect.rect.yMax-40,0));
                factoryRect.position+=expansionTopLeft-factoryRect.TransformPoint(new Vector3(factoryRect.rect.xMin,factoryRect.rect.yMax,0));
                FactoryState.Buffer(state.Input,1102).Add(2,8); FactoryState.Buffer(state.Output,1202).Add(3,0);
                state.Progress=30; state.Duration=60; state.Status="生产中";
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                Check(!view.recipeGroup.gameObject.activeSelf && view.produceGroup.gameObject.activeSelf && view.servingBox.active,"recipe activates native production and ingredient controls");
                Check(view.playerInventory.active && view.playerInventoryGroup.gameObject.activeSelf && view.windowTrans.sizeDelta==new Vector2(580,460),"native backpack grid controls window proportions");
                Check(Vector3.Distance(expansionTopLeft,factoryRect.TransformPoint(new Vector3(factoryRect.rect.xMin,factoryRect.rect.yMax,0)))<0.1f
                    && WindowFits(factoryRect,drag.screenRect),"opening the backpack preserves the title position near the top screen edge");
                Check(view.productIcon0.sprite==LDB.items.Select(1202).iconSprite && view.productCountText0.text=="3","native output slot shows product and buffer count");
                var serving=(StorageComponent)AccessTools.Field(type,"serving").GetValue(null);
                Check(serving.grids[0].itemId==1102 && serving.grids[0].count==2 && serving.grids[0].inc==8,"native ingredient grid reflects item counts and spray points");
                Check(!view.copyButton.gameObject.activeSelf && !view.pasteButton.gameObject.activeSelf,"factory window hides bulk refill and retrieval buttons");
                var gridClick=AccessTools.Method(typeof(UIStorageGrid),"OnGridMouseDown");
                package.grids[1].inc=48;
                player.SetHandItems(1102,6,1);
                gridClick.Invoke(view.servingBox,new object[]{0,0,false,false,player});
                Check(state.Input[1102].Count==8 && state.Input[1102].Inc==32 && player.inhandItemCount==0,
                    "manual hand insertion exceeds the automatic two-batch target and preserves proliferation");
                int returnedOre=package.GetItemCount(1102);
                gridClick.Invoke(view.servingBox,new object[]{0,0,false,true,player});
                Check(state.Input[1102].Count==0 && state.Input[1102].Inc==0 && package.GetItemCount(1102)==returnedOre+8,
                    "manual ingredient retrieval returns the full enlarged cache to the backpack");
                state.Input[1102].Count=0;state.Input[1102].Inc=0;
                int inputCapacity=FactoryState.ManualInputLimit(LDB.recipes.Select(6).ItemCounts[0],LDB.recipes.Select(6).Type);
                int availableOre=inputCapacity+7;
                package.grids[1]=new StorageComponent.GRID {itemId=1102,count=availableOre,inc=availableOre*4,stackSize=100};
                gridClick.Invoke(view.playerInventory,new object[]{1,0,false,true,player});
                Check(state.Input[1102].Count==serving.grids[0].stackSize && state.Input[1102].Count==inputCapacity
                    && state.Input[1102].Count>FactoryState.InputLimit(LDB.recipes.Select(6).ItemCounts[0])
                    && state.Input[1102].Inc==4*inputCapacity && package.grids[1].count==availableOre-inputCapacity && package.grids[1].inc==4*(availableOre-inputCapacity)
                    && player.inhandItemCount==0,"native Ctrl-click fills live ingredient capacity and preserves excess items and proliferation");
                Check(inputCapacity==40 && view.servingBox.lackParam==20,"assembling uses the native twenty-batch manual capacity");
                using(var memory=new MemoryStream()) {
                    state.Write(new BinaryWriter(memory));memory.Position=0;
                    var restored=FactoryState.Read(new BinaryReader(memory));
                    Check(restored.Input[1102].Count==inputCapacity && restored.Input[1102].Inc==inputCapacity*4,
                        "manual stock above the automatic target survives a save roundtrip");
                }
                gridClick.Invoke(view.playerInventory,new object[]{1,0,false,true,player});
                Check(state.Input[1102].Count==inputCapacity && package.grids[1].count+player.inhandItemCount==availableOre-inputCapacity
                    && package.grids[1].inc+player.inhandItemInc==4*(availableOre-inputCapacity),"full ingredient cache preserves items through native hand pickup fallback");
                package.AddItemStacked(player.inhandItemId,player.inhandItemCount,player.inhandItemInc,out int unusedInc);
                player.SetHandItems(0,0);
                gridClick.Invoke(view.playerInventory,new object[]{0,0,false,true,player});
                Check(package.grids[0].count+player.inhandItemCount==20 && state.Input[1102].Count==inputCapacity
                    && !state.Input.ContainsKey(1101),"Ctrl-click keeps unrelated items out of the ingredient cache");
                package.AddItemStacked(player.inhandItemId,player.inhandItemCount,player.inhandItemInc,out unusedInc);
                player.SetHandItems(0,0);
                Check(view.incSwitchButton.interactable && view.incSwitchButtonThumb.rectTransform.anchoredPosition.x==-10,"productive recipe enables the native mode switch");
                state.ExtraProgress=0.25;state.BatchBonus=0.25;state.BatchAccMode=false;
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                Check(Mathf.Abs(view.extraProductProgress0.fillAmount-0.375f)<0.0001f
                    && view.extraProductProgress1.fillAmount==view.extraProductProgress0.fillAmount,
                    "both native bonus rings include the current half-complete production batch");
                Check(state.ExtraProgress==0.25 && state.Output[1202].Count==3,"refreshing bonus progress does not settle production early");
                state.Progress=45;
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                Check(Mathf.Abs(view.extraProductProgress0.fillAmount-0.4375f)<0.0001f,"bonus progress advances before the recipe completes");
                state.ForceAccMode=true;
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                Check(Mathf.Abs(view.extraProductProgress0.fillAmount-0.4375f)<0.0001f,"selecting next-batch acceleration preserves current bonus progress");
                state.BatchAccMode=true;state.BatchBonus=0;
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                Check(view.extraProductProgress0.fillAmount==0.25f,"an accelerated batch preserves banked bonus progress without increasing it");
                state.ForceAccMode=false;state.BatchAccMode=false;state.BatchBonus=0.25;state.Progress=30;
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                CaptureWindow(window);
                view.incSwitchButton.onClick.Invoke(); Check(state.ForceAccMode,"native mode control changes factory preference");
                var dual=LDB.recipes.dataArray.First(r=>r.Results.Length==2);
                state.ChangeRecipe(dual.ID);
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                Check(view.productGroup.sizeDelta==new Vector2(128,64) && view.speedGroup.anchoredPosition.x==144 && view.servingGroup.anchoredPosition.x==224,
                    "dual-product recipes retain native output, speed and ingredient spacing");
                Check(view.productIcon1.sprite==LDB.items.Select(dual.Results[1]).iconSprite && view.productIcon1.gameObject.activeSelf,"second native product slot shows the secondary result");
                var smelting=LDB.recipes.dataArray.First(recipe=>recipe.Type==ERecipeType.Smelt && recipe.Items.Length>0);
                state.ChangeRecipe(smelting.ID);
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                var smeltingStorage=(StorageComponent)AccessTools.Field(type,"serving").GetValue(null);
                Check(view.servingBox.lackParam==50 && Enumerable.Range(0,smelting.Items.Length).All(i=>smeltingStorage.grids[i].stackSize==smelting.ItemCounts[i]*50),
                    "smelting uses the native fifty-batch manual capacity");
                state.ChangeRecipe(Prototypes.RecipeId); state.ForceAccMode=false;
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                Check(!view.incSwitchButton.interactable && view.incSwitchButtonThumb.rectTransform.anchoredPosition.x==10 && !state.ForceAccMode,"nonproductive recipe locks acceleration without changing preference");
                int ironBefore=package.grids[0].count;
                gridClick.Invoke(view.playerInventory,new object[]{0,0,false,true,player});
                int acceptedIron=Math.Min(ironBefore,FactoryState.ManualInputLimit(4,ERecipeType.Assemble));
                Check(state.Input[1101].Count==acceptedIron && package.grids[0].count==ironBefore-acceptedIron,"Ctrl-click follows the new recipe ingredient filters and capacities");
                var closedInventory=view.playerInventory;var closedServing=view.servingBox;
                var savedFactoryTopLeft=factoryRect.TransformPoint(new Vector3(factoryRect.rect.xMin,factoryRect.rect.yMax,0));
                AccessTools.Method(type,"Hide").Invoke(null,null);
                Check(factoryRect.anchoredPosition==originalWindowPosition,"closing the factory restores the native spray coater window position");
                var opened=(List<UIStorageGrid>)AccessTools.Field(typeof(UIStorageGrid),"openedStorages").GetValue(null);
                Check(!opened.Contains(closedInventory) && !opened.Contains(closedServing),"closing factory removes both grids from native transfer targets");
                state.ChangeRecipe(6);
                state.Input[1102].Count=0;state.Input[1102].Inc=0;
                AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                view=(UIAssemblerWindow)AccessTools.Field(type,"view").GetValue(null);
                drag=view.GetComponent<UIWindowDrag>();
                Check(Vector3.Distance(savedFactoryTopLeft,factoryRect.TransformPoint(new Vector3(factoryRect.rect.xMin,factoryRect.rect.yMax,0)))<0.1f,
                    "reopening the factory retains its title position independently of the native coater");
                int oreBefore=package.grids[1].count;
                gridClick.Invoke(view.playerInventory,new object[]{1,0,false,true,player});
                int acceptedOre=Math.Min(oreBefore,inputCapacity);
                Check(state.Input[1102].Count==acceptedOre && package.grids[1].count==oreBefore-acceptedOre
                    && opened.Count(g=>g==view.servingBox)==1 && opened.Count(g=>g==view.playerInventory)==1,
                    "reopened factory transfers into its current ingredient cache once");
                var originalRecipes=LDB.recipes.dataArray;
                var six=(RecipeProto)AccessTools.Method(typeof(object),"MemberwiseClone").Invoke(LDB.recipes.Select(6),null);
                six.ID=Enumerable.Range(11000,1000).First(id=>LDB.recipes.Select(id)==null);six.Name="RuntimeChecks.SixProducts";six.TimeSpend=120;
                var materials=LDB.items.dataArray.Where(item=>item.ID>1000 && item.ID<10000 && !item.IsEntity && item.iconSprite!=null).Take(12).Select(item=>item.ID).ToArray();
                six.Items=materials.Take(6).ToArray();six.ItemCounts=Enumerable.Repeat(1,6).ToArray();
                six.Results=materials.Skip(6).Take(6).ToArray();six.ResultCounts=Enumerable.Range(1,6).ToArray();
                try {
                    LDB.recipes.dataArray=originalRecipes.Concat(new[]{six}).ToArray();LDB.recipes.OnAfterDeserialize();GameMain.history.recipeUnlocked.Add(six.ID);
                    state.ChangeRecipe(six.ID);state.Input.Clear();state.Pending.Clear();state.Output.Clear();
                    for(int i=0;i<6;i++) {
                        package.grids[i]=new StorageComponent.GRID {itemId=six.Items[i],count=4,inc=32,stackSize=100};
                        FactoryState.Buffer(state.Output,six.Results[i]).Add(six.ResultCounts[i],0);
                    }
                    AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                    var slots=(System.Collections.IList)AccessTools.Field(type,"extraProducts").GetValue(null);
                    Check(view.servingBox.colCount==3 && view.servingBox.rowCount==2 && slots.Count==4
                        && view.windowTrans.sizeDelta==new Vector2(580,524),"six ingredients and six products fit two native rows above the backpack");
                    Check(Vector3.Distance(savedFactoryTopLeft,factoryRect.TransformPoint(new Vector3(factoryRect.rect.xMin,factoryRect.rect.yMax,0)))<0.1f
                        && WindowFits(factoryRect,drag.screenRect),"adding the second recipe row preserves the title and stays on screen");
                    var actualScreen=drag.screenRect;
                    var smallScreen=new GameObject("factory-window-screen-check",typeof(RectTransform)).GetComponent<RectTransform>();
                    smallScreen.SetParent(actualScreen,false);smallScreen.sizeDelta=new Vector2(800,600);
                    try {
                        drag.screenRect=smallScreen;
                        var offscreenTopLeft=smallScreen.TransformPoint(new Vector3(smallScreen.rect.xMax-80,smallScreen.rect.yMax+100,0));
                        factoryRect.position+=offscreenTopLeft-factoryRect.TransformPoint(new Vector3(factoryRect.rect.xMin,factoryRect.rect.yMax,0));
                        AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                        Check(WindowFits(factoryRect,smallScreen),"a changed UI canvas brings the full six-product window back inside its bounds");
                    } finally {drag.screenRect=actualScreen;UnityEngine.Object.DestroyImmediate(smallScreen.gameObject);}
                    AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                    for(int i=0;i<6;i++)gridClick.Invoke(view.playerInventory,new object[]{i,0,false,true,player});
                    Check(six.Items.All(item=>state.Input[item].Count==4 && state.Input[item].Inc==32),"all six native ingredient slots accept manual stock above two batches with proliferation");
                    bool collected=true;
                    for(int i=0;i<6;i++) {
                        var button=i==0?view.productButton0:i==1?view.productButton1:(UIButton)AccessTools.Field(slots[i-2].GetType(),"Button").GetValue(slots[i-2]);
                        int before=package.GetItemCount(six.Results[i]);button.button.onClick.Invoke();
                        collected &= state.Output[six.Results[i]].Count==0 && package.GetItemCount(six.Results[i])==before+six.ResultCounts[i];
                    }
                    Check(collected,"all six native product buttons retrieve their corresponding output");
                    foreach(var extra in slots) {
                        var root=(RectTransform)AccessTools.Field(extra.GetType(),"Root").GetValue(extra);
                        Check(root.GetComponentsInChildren<UnityEngine.UI.Button>(true).Count(button=>button.gameObject.activeSelf)==1,"additional product slots contain one active product action");
                    }
                    CaptureWindow(window,"factory-six-products.png");
                    state.ChangeRecipe(6);AccessTools.Method(type,"Refresh").Invoke(null,new object[]{window});
                    Check(view.windowTrans.sizeDelta.y==460 && slots.Cast<object>().All(slot=>!((RectTransform)AccessTools.Field(slot.GetType(),"Root").GetValue(slot)).gameObject.activeSelf),"returning to two outputs hides extra rows and restores native window height");
                } finally { LDB.recipes.dataArray=originalRecipes;LDB.recipes.OnAfterDeserialize(); }
            } finally {
                AccessTools.Method(type,"Hide").Invoke(null,null);
                Check(((RectTransform)window.transform).sizeDelta==originalWindowSize,"vanilla window size restores after factory closes");
                Check(((RectTransform)window.transform).anchoredPosition==originalWindowPosition,"vanilla window position restores after factory closes");
                window.factory=previousFactory; window.traffic=previousTraffic;
                AccessTools.Field(typeof(UISpraycoaterWindow),"_spraycoaterId").SetValue(window,0);
                GameMain.data=previousData; package.Free();
            }
        });
        File.WriteAllLines(Path.Combine(Output,"results.txt"),results.Concat(new[]{"Failures="+failures}));
        Logger.LogInfo("Spray Factory checks completed: failures="+failures);
        Application.Quit(failures==0?0:1);
    }

    private static bool WindowFits(RectTransform window,RectTransform screen)
    {
        var corners=new Vector3[4];window.GetWorldCorners(corners);
        return corners.All(corner=>{
            var point=screen.InverseTransformPoint(corner);
            return point.x>=screen.rect.xMin-0.1f && point.x<=screen.rect.xMax+0.1f
                && point.y>=screen.rect.yMin-0.1f && point.y<=screen.rect.yMax+0.1f;
        });
    }
    private void CaptureWindow(ManualBehaviour window,string filename="factory-ui.png")
    {
        var canvasObject=new GameObject("factory-ui-preview",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler));
        var cameraObject=new GameObject("factory-ui-camera",typeof(Camera));
        var previousParent=window.transform.parent;
        var previousPosition=((RectTransform)window.transform).anchoredPosition;
        var previousAnchors=new[]{((RectTransform)window.transform).anchorMin,((RectTransform)window.transform).anchorMax};
        bool previousActive=window.gameObject.activeSelf;
        var target=new RenderTexture(1000,620,24);
        var previousTarget=RenderTexture.active;
        try {
            var camera=cameraObject.GetComponent<Camera>();
            camera.orthographic=true; camera.orthographicSize=310; camera.transform.position=new Vector3(0,0,-10);
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(0.035f,0.055f,0.075f,1);
            camera.targetTexture=target; camera.cullingMask=-1;
            var canvas=canvasObject.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
            var scaler=canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
            window.transform.SetParent(canvasObject.transform,false);
            var rect=(RectTransform)window.transform; rect.anchorMin=rect.anchorMax=new Vector2(0.5f,0.5f);
            rect.anchoredPosition=Vector2.Scale(rect.pivot-new Vector2(0.5f,0.5f),rect.sizeDelta);
            window.gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=target;
            var texture=new Texture2D(1000,620,TextureFormat.RGB24,false);
            try {texture.ReadPixels(new Rect(0,0,1000,620),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Output,filename),texture.EncodeToPNG());}
            finally {UnityEngine.Object.DestroyImmediate(texture);}
        } finally {
            RenderTexture.active=previousTarget; window.gameObject.SetActive(previousActive);
            window.transform.SetParent(previousParent,false);
            var rect=(RectTransform)window.transform; rect.anchorMin=previousAnchors[0];rect.anchorMax=previousAnchors[1];rect.anchoredPosition=previousPosition;
            UnityEngine.Object.DestroyImmediate(canvasObject);UnityEngine.Object.DestroyImmediate(cameraObject);target.Release();UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
