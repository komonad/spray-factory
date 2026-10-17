using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SprayFactory
{
    internal static class FactoryRecipePicker
    {
        private static UIRecipePicker owned;
        private static UIWindowDrag drag;
        private static Text title;
        private static Vector2 titleSize;
        private static bool titleRaycast;

        internal static void Open(RectTransform origin,Action<RecipeProto> onReturn)
        {
            var picker=UIRoot.instance.uiGame.recipePicker;
            if(picker.active) { if(owned==picker) Close(); return; }
            if(!picker.inited) return;
            title=picker.transform.Find("title-text").GetComponent<Text>();
            titleSize=title.rectTransform.sizeDelta;titleRaycast=title.raycastTarget;
            if(drag==null)
            {
                drag=picker.gameObject.AddComponent<UIWindowDrag>();
                drag.enabled=false;
                drag.dragTrigger=title.gameObject.AddComponent<EventTrigger>();
                drag.dragTrans=drag.refTrans=picker.pickerTrans;
                var native=UIRoot.instance.uiGame.assemblerWindow.GetComponent<UIWindowDrag>();
                drag.screenRect=native.screenRect;
                drag.dragThreshold=native.dragThreshold;drag.damping=native.damping;drag.spring=native.spring;
                drag.hasPopupAudio=false;
            }
            title.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                picker.pickerTrans.rect.width-title.rectTransform.anchoredPosition.x-50);
            title.raycastTarget=true;
            owned=picker;drag.enabled=true;
            UIRecipePicker.Popup(Vector2.zero,onReturn,ERecipeType.None);
            Align(picker.pickerTrans,origin,drag.screenRect);
        }

        private static void Align(RectTransform target,RectTransform origin,RectTransform screen)
        {
            var topLeft=origin.TransformPoint(new Vector3(origin.rect.xMin,origin.rect.yMax,0));
            target.position+=topLeft-target.TransformPoint(new Vector3(target.rect.xMin,target.rect.yMax,0));
            var corners=new Vector3[4];target.GetWorldCorners(corners);
            var min=screen.InverseTransformPoint(corners[0]);var max=screen.InverseTransformPoint(corners[2]);
            float x=min.x<screen.rect.xMin?screen.rect.xMin-min.x:Mathf.Min(0,screen.rect.xMax-max.x);
            float y=max.y>screen.rect.yMax?screen.rect.yMax-max.y:Mathf.Max(0,screen.rect.yMin-min.y);
            target.position+=screen.TransformVector(new Vector3(x,y,0));
        }

        internal static void Close()
        {
            if(owned==null) return;
            if(owned.active) UIRecipePicker.Close();
            else Release(owned);
        }

        internal static void Release(UIRecipePicker picker)
        {
            if(owned!=picker) return;
            drag.enabled=false;
            title.rectTransform.sizeDelta=titleSize;title.raycastTarget=titleRaycast;
            owned=null;
        }
    }

    [HarmonyPatch(typeof(UIRecipePicker),"_OnClose")]
    internal static class RecipePickerClosePatch
    {
        private static void Postfix(UIRecipePicker __instance) { FactoryRecipePicker.Release(__instance); }
    }
}
