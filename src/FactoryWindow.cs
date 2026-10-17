using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SprayFactory
{
    internal static class FactoryWindow
    {
        private static UISpraycoaterWindow owner;
        private static UIAssemblerWindow view;
        private static readonly Dictionary<GameObject,bool> hidden=new Dictionary<GameObject,bool>();
        private static Vector2 originalSize;
        private static StorageComponent serving;
        private static int servingRecipe;
        private static readonly StringBuilder powerServed=new StringBuilder("         W     %",20);

        internal static bool Matches(UISpraycoaterWindow window)
        {
            return window.factory!=null && window.traffic!=null && window.spraycoaterId>0 &&
                FactoryRuntime.IsFactory(window.factory,window.traffic.spraycoaterPool[window.spraycoaterId].entityId);
        }
        internal static void Show(UISpraycoaterWindow window)
        {
            if(owner==window && view!=null) return;
            Hide(); owner=window;
            originalSize=((RectTransform)owner.transform).sizeDelta;
            foreach(Transform child in owner.transform)
            { hidden[child.gameObject]=child.gameObject.activeSelf; child.gameObject.SetActive(false); }
            view=UnityEngine.Object.Instantiate(UIRoot.instance.uiGame.assemblerWindow,owner.transform,false);
            view.name="spray-factory-panel";
            view.enabled=false;
            var rect=(RectTransform)view.transform;
            rect.anchorMin=rect.anchorMax=rect.pivot;
            rect.anchoredPosition=Vector2.zero;
            rect.localScale=Vector3.one;
            foreach(var button in view.GetComponentsInChildren<Button>(true)) button.onClick=new Button.ButtonClickedEvent();
            SetText(view.titleText,"喷涂工厂");
            Bind(view.recipeButton.button,SelectRecipe);
            Bind(view.resetButton.button,()=>WithState(state=>state.ChangeRecipe(0)));
            Bind(view.productButton0.button,()=>TakeOutput(0));
            Bind(view.productButton1.button,()=>TakeOutput(1));
            Bind(view.incSwitchButton,()=>WithState(state=>state.ForceAccMode=!state.ForceAccMode));
            Bind(view.copyButton.button,FillInputs); SetText(view.copyButton.GetComponentInChildren<Text>(true),"补料");
            Bind(view.pasteButton.button,ReturnAll); SetText(view.pasteButton.GetComponentInChildren<Text>(true),"取回");
            var copyRect=(RectTransform)view.copyButton.transform;
            var returnRect=(RectTransform)view.pasteButton.transform;
            returnRect.SetParent(copyRect.parent,false);
            returnRect.anchorMin=copyRect.anchorMin; returnRect.anchorMax=copyRect.anchorMax; returnRect.pivot=copyRect.pivot;
            returnRect.sizeDelta=copyRect.sizeDelta;
            returnRect.anchoredPosition=copyRect.anchoredPosition-Vector2.right*(copyRect.rect.width+6);
            Bind(view.transform.Find("panel-bg/btn-box/close-wnd-btn").GetComponent<Button>(),()=>UIRoot.instance.uiGame.ShutSpraycoaterWindow());
            view.menuButton.gameObject.SetActive(false);
            view.alarmSwitchButton.gameObject.SetActive(false); view.alarmSwitchText.gameObject.SetActive(false);
            InitializeGrid(view.playerInventory); InitializeGrid(view.servingBox);
            view.servingBox.beforeManualChange+=SyncInputs;
            var drag=view.GetComponent<UIWindowDrag>();
            drag.dragTrans=(RectTransform)owner.transform; drag.refTrans=drag.dragTrans;
            view.gameObject.SetActive(true);
        }
        private static void SetText(Text label,string text)
        {
            var localizer=label.GetComponent<Localizer>(); if(localizer!=null) localizer.enabled=false;
            label.text=text;
        }
        private static void InitializeGrid(UIStorageGrid grid)
        {
            // Runtime visuals and buffers belong to each grid instance.
            foreach(var tip in grid.GetComponentsInChildren<Component>(true).Where(c=>c!=null && c.GetType().Name=="UICancelFilterTip")) UnityEngine.Object.DestroyImmediate(tip.gameObject);
            foreach(var label in grid.GetComponentsInChildren<Text>(true))
                if(label!=grid.prefabNumText && label.transform.parent==grid.rectTrans && label.name==grid.prefabNumText.name+"(Clone)")
                    UnityEngine.Object.DestroyImmediate(label.gameObject);
            grid._Create();
            foreach(var tip in grid.GetComponentsInChildren<Component>(true).Where(c=>c!=null && c.GetType().Name=="UICancelFilterTip")) tip.gameObject.SetActive(false);
        }
        private static void RefreshPower()
        {
            var component=owner.traffic.spraycoaterPool[owner.spraycoaterId];
            var system=owner.factory.powerSystem;float ratio=FactoryPower.Ratio(system,component.pcId);
            view.powerIcon.gameObject.SetActive(true);
            if(ratio<0.1f)
            {
                view.powerText.text="未供电".Translate();view.powerIcon.color=Color.clear;view.powerText.color=view.powerOffColor;
                return;
            }
            long served=(long)(system.consumerPool[component.pcId].requiredEnergy*60*ratio+0.5);
            StringBuilderUtility.WriteKMG(powerServed,8,served);
            StringBuilderUtility.WriteUInt(powerServed,12,3,(uint)(ratio*100));
            view.powerText.text=powerServed.ToString();
            view.powerIcon.color=ratio>=0.999f?view.powerNormalIconColor:view.powerLowIconColor;
            view.powerText.color=ratio>=0.999f?view.powerNormalColor:view.powerLowColor;
        }
        private static void Bind(Button button,Action action)
        {
            button.onClick=new Button.ButtonClickedEvent(); button.onClick.AddListener(()=>action());
            var ui=button.GetComponent<UIButton>(); if(ui!=null) ui.tips=default(UIButton.TipSettings);
        }
        private static void WithState(Action<FactoryState> action)
        {
            if(owner==null || !Matches(owner)) return;
            int entity=owner.traffic.spraycoaterPool[owner.spraycoaterId].entityId;
            var state=FactoryStore.Get(owner.factory.planetId,entity);
            lock(state.Sync)
            {
                int previous=state.RecipeId; action(state);
                if(previous!=state.RecipeId) FactoryRuntime.UpdateSign(owner.factory,entity,LDB.recipes.Select(state.RecipeId),false);
            }
        }
        private static void SelectRecipe()
        {
            if(owner==null) return;
            int planet=owner.factory.planetId, entity=owner.traffic.spraycoaterPool[owner.spraycoaterId].entityId;
            FactoryRecipePicker.Open((RectTransform)owner.transform,recipe=>{
                if(recipe==null || owner==null || !Matches(owner) || owner.factory.planetId!=planet ||
                    owner.traffic.spraycoaterPool[owner.spraycoaterId].entityId!=entity) return;
                WithState(state=>state.ChangeRecipe(recipe.ID));
            });
        }
        private static void FillInputs()
        {
            WithState(state=>{
                var recipe=LDB.recipes.Select(state.RecipeId); var player=GameMain.mainPlayer;
                if(recipe==null || player==null || !GameMain.history.RecipeUnlocked(recipe.ID)) return;
                foreach(int item in recipe.Items)
                {
                    int id=item,count=state.Acceptable(recipe,item);
                    player.package.TakeTailItems(ref id,ref count,out int inc);
                    if(count>0) FactoryState.Buffer(state.Input,item).Add(count,inc);
                }
            });
        }
        private static void ReturnAll()
        {
            WithState(state=>{
                if(GameMain.mainPlayer!=null) FactoryRuntime.ReturnItems(GameMain.mainPlayer,owner.traffic.spraycoaterPool[owner.spraycoaterId].entityId,state);
            });
        }
        private static void TakeOutput(int index)
        {
            WithState(state=>{
                var recipe=LDB.recipes.Select(state.RecipeId);
                if(GameMain.mainPlayer==null || recipe==null || index>=recipe.Results.Length) return;
                int item=recipe.Results[index]; var buffer=FactoryState.Buffer(state.Output,item);
                GameMain.mainPlayer.TryAddItemToPackage(item,buffer.Count,buffer.Inc,true,owner.traffic.spraycoaterPool[owner.spraycoaterId].entityId);
                buffer.Count=buffer.Inc=0;
            });
        }
        private static void FreeServing()
        {
            if(serving==null) return;
            serving.onStorageChange-=ApplyInputs;
            view.servingBox._Free(); serving.Free(); serving=null; servingRecipe=0;
        }
        private static void ApplyInputs()
        {
            WithState(state=>{
                if(serving==null || state.RecipeId!=servingRecipe) return;
                foreach(var grid in serving.grids)
                {
                    var buffer=FactoryState.Buffer(state.Input,grid.filter);
                    buffer.Count=grid.count; buffer.Inc=grid.inc;
                }
            });
        }
        private static void SyncInputs()
        {
            WithState(state=>{
                var recipe=LDB.recipes.Select(state.RecipeId);
                if(serving==null || recipe==null || state.RecipeId!=servingRecipe) return;
                for(int i=0;i<recipe.Items.Length;i++)
                {
                    var buffer=FactoryState.Buffer(state.Input,recipe.Items[i]);
                    serving.grids[i].count=buffer.Count; serving.grids[i].inc=buffer.Inc;
                }
                view.servingBox.OnStorageContentChanged();
            });
        }
        internal static void Refresh(UISpraycoaterWindow window)
        {
            Show(window);
            WithState(state=>{
                var recipe=LDB.recipes.Select(state.RecipeId);
                bool hasRecipe=recipe!=null, accelerate=hasRecipe && (!recipe.productive || state.ForceAccMode);
                view.recipeGroup.gameObject.SetActive(!hasRecipe); view.produceGroup.gameObject.SetActive(hasRecipe);
                view.incInfoGroup.gameObject.SetActive(hasRecipe);
                RefreshPower();
                view.stateText.text=state.Status; view.stateText.color=state.Status=="生产中"?view.workNormalColor:view.workStoppedColor;
                view.resetButton.button.interactable=hasRecipe;
                view.copyButton.gameObject.SetActive(hasRecipe);
                view.pasteButton.gameObject.SetActive(hasRecipe || state.Input.Values.Any(b=>b.Count>0) || state.Output.Values.Any(b=>b.Count>0) || state.Pending.Values.Any(b=>b.Count>0));
                view.incSwitchButton.interactable=hasRecipe && recipe.productive;
                view.incSwitchButtonImage.color=accelerate?view.incButtonColor2:view.incButtonColor1;
                view.incSwitchButtonThumb.rectTransform.anchoredPosition=new Vector2(accelerate?10:-10,0);
                view.incEffectTypeText.text=accelerate?"加速生产":"额外产出";
                view.incEffectTypeText.color=accelerate?view.incTextColor2:view.incTextColor1;
                view.incEffectValueText2.text="+0.0 %";
                SetText(view.transferTipText,"原料缓存：2 轮    满 "+FactoryRuntime.OutputStackLimit()+" 层输出");
                if(servingRecipe!=state.RecipeId)
                {
                    FreeServing();
                    if(hasRecipe && recipe.Items.Length>0)
                    {
                        serving=new StorageComponent(recipe.Items.Length) {type=EStorageType.Filtered}; servingRecipe=state.RecipeId;
                        for(int i=0;i<recipe.Items.Length;i++) serving.SetFilterDirect(i,recipe.Items[i],FactoryState.InputLimit(recipe.ItemCounts[i]));
                        serving.onStorageChange+=ApplyInputs;
                        view.servingBox.colCount=recipe.Items.Length; view.servingBox.lackParam=2;
                        view.servingBox._Init(serving); view.servingBox._Open();
                    }
                }
                view.servingGroup.gameObject.SetActive(serving!=null);
                SyncInputs();
                if(hasRecipe)
                {
                    FactoryRates.Calculate(state,recipe,out float cycles,out float multiplier);
                    view.speedText.text=(cycles*recipe.ResultCounts[0]*multiplier).ToString("0.0")+"每分钟".Translate();
                    view.incEffectValueText.text="+"+((accelerate?Math.Max(0,cycles*recipe.TimeSpend/3600-1):multiplier-1)*100).ToString("0.0")+" %";
                    view.incEffectValueText.color=view.incEffectTypeText.color;
                    float progress=state.Duration>0?(float)state.Progress/state.Duration:0;
                    view.productProgress0.fillAmount=view.productProgress1.fillAmount=progress;
                    float extraProgress=Mathf.Clamp01((float)(state.ExtraProgress+(state.BatchAccMode?0:state.BatchBonus*progress)));
                    view.extraProductProgress0.fillAmount=view.extraProductProgress1.fillAmount=extraProgress;
                    for(int i=0;i<2;i++)
                    {
                        bool visible=i<recipe.Results.Length;
                        var button=i==0?view.productButton0:view.productButton1;
                        var icon=i==0?view.productIcon0:view.productIcon1;
                        var count=i==0?view.productCountText0:view.productCountText1;
                        var ring=i==0?view.productProgress0:view.productProgress1;
                        var extraRing=i==0?view.extraProductProgress0:view.extraProductProgress1;
                        button.gameObject.SetActive(visible); icon.gameObject.SetActive(visible); count.gameObject.SetActive(visible);
                        ring.gameObject.SetActive(visible); extraRing.gameObject.SetActive(visible);
                        if(!visible) continue;
                        int item=recipe.Results[i]; var buffer=FactoryState.Buffer(state.Output,item);
                        icon.sprite=LDB.items.Select(item)?.iconSprite; count.text=buffer.Count.ToString();
                        button.button.interactable=buffer.Count>0; button.tips.itemId=item; button.tips.type=UIButton.ItemTipType.Item;
                        button.tips.corner=2; button.tips.delay=0.2f;
                    }
                    int width=recipe.Results.Length==1?64:128;
                    view.productGroup.sizeDelta=new Vector2(width,64);
                    view.speedGroup.anchoredPosition=new Vector2(width+16,view.speedGroup.anchoredPosition.y);
                    view.servingGroup.anchoredPosition=new Vector2(width+96,view.speedGroup.anchoredPosition.y);
                    int activeArrow=(int)(progress*view.speedArrows.Length)%view.speedArrows.Length;
                    for(int i=0;i<view.speedArrows.Length;i++) view.speedArrows[i].color=state.Status=="生产中" && i==activeArrow?view.marqueeOnColor:view.marqueeOffColor;
                }
                var player=GameMain.mainPlayer;
                if(hasRecipe && player!=null && !view.playerInventory.inited)
                {
                    view.playerInventory.colCount=player.GetPackageColumnCount(); view.playerInventory._Init(player.package); view.playerInventory._Open();
                }
                if(!hasRecipe && view.playerInventory.inited) view.playerInventory._Free();
                view.playerInventoryGroup.gameObject.SetActive(hasRecipe && player!=null);
                view.playerInventoryGroup.anchoredPosition=new Vector2(0,-220);
                float widthWindow=player!=null?80+player.GetPackageColumnCount()*50:((RectTransform)UIRoot.instance.uiGame.assemblerWindow.transform).sizeDelta.x;
                var size=new Vector2(widthWindow,(view.playerInventory.active?view.playerInventory.rowCount*50+220:270)+(hasRecipe?40:0));
                view.windowTrans.sizeDelta=size; ((RectTransform)owner.transform).sizeDelta=size;
            });
            if(GameMain.mainPlayer!=null)
            {
                if(view.servingBox.active) view.servingBox._Update();
                if(view.playerInventory.active) view.playerInventory._Update();
            }
        }
        internal static void Hide()
        {
            FactoryRecipePicker.Close();
            if(view!=null)
            {
                FreeServing(); view.servingBox._Destroy(); view.playerInventory._Destroy();
                view.gameObject.SetActive(false); UnityEngine.Object.Destroy(view.gameObject); view=null;
            }
            if(owner!=null) ((RectTransform)owner.transform).sizeDelta=originalSize;
            foreach(var entry in hidden) if(entry.Key!=null) entry.Key.SetActive(entry.Value);
            hidden.Clear(); owner=null;
        }
    }
    [HarmonyPatch(typeof(UISpraycoaterWindow),"_OnUpdate")]
    internal static class WindowUpdatePatch
    {
        private static bool Prefix(UISpraycoaterWindow __instance)
        {
            if(FactoryWindow.Matches(__instance)) { FactoryWindow.Refresh(__instance); return false; }
            FactoryWindow.Hide(); return true;
        }
    }
    [HarmonyPatch(typeof(UISpraycoaterWindow),"_OnClose")]
    internal static class WindowClosePatch
    {
        private static void Prefix() { FactoryWindow.Hide(); }
    }
}
