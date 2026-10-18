using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using xiaoye97;

namespace SprayFactory
{
    internal static class FactoryBuildMenu
    {
        internal static void Place(ItemProto item,ItemProto coater)
        {
            var used=new HashSet<int>(LDB.items.dataArray.Where(i=>i.ID!=item.ID).Select(i=>i.BuildIndex));
            var lower=(ItemProto[,])AccessTools.Field(typeof(UIBuildMenu),"protos").GetValue(null);
            int preferred=coater.BuildIndex/100;
            var categories=new[]{preferred,4}.Concat(Enumerable.Range(1,9)).Distinct().Where(c=>c>=1 && c<=9).ToArray();
            if(Chainloader.PluginInfos.TryGetValue("Gnimaerd.DSP.plugin.BuildBarTool",out var plugin))
            {
                var type=plugin.Instance.GetType();
                var upper=(ItemProto[,])AccessTools.Field(type,"protos").GetValue(null);
                var reserved=(int[,])AccessTools.Field(type,"protoIds").GetValue(null);
                int sourceColumn=coater.BuildIndex%100;
                if(sourceColumn>20) sourceColumn-=20;
                var columns=new[]{sourceColumn}.Concat(Enumerable.Range(1,10)).Distinct().Where(n=>n>=1 && n<=10);
                foreach(int category in categories)
                foreach(int column in columns)
                {
                    int buildIndex=category*100+20+column;
                    if(used.Contains(buildIndex) || upper[category,column]!=null || reserved[category,column]!=0) continue;
                    var api=AccessTools.TypeByName("BuildBarTool.BuildBarTool");
                    var bind=AccessTools.Method(api,"SetBuildBar",new[]{typeof(int),typeof(int),typeof(int),typeof(bool)});
                    if(!(bool)bind.Invoke(null,new object[]{category,column,item.ID,true})) continue;
                    item.BuildIndex=buildIndex;
                    AccessTools.Method(type,"LoadFromBuildIndex").Invoke(null,null);
                    Plugin.Log.LogInfo("Factory build menu: category="+category+" row=upper slot="+column);
                    return;
                }
            }
            var buttons=UIRoot.instance?.uiGame?.buildMenu?.childButtons;
            int last=buttons==null?10:Math.Min(10,buttons.Length-1);
            foreach(int category in categories)
            for(int column=1;column<=last;column++)
            {
                int buildIndex=category*100+column;
                if(used.Contains(buildIndex) || (lower[category,column]!=null && lower[category,column].ID!=item.ID)
                    || (buttons!=null && buttons[column]==null)) continue;
                item.BuildIndex=buildIndex;
                LDBTool.SetBuildBar(category,column,item.ID);
                Plugin.Log.LogInfo("Factory build menu: category="+category+" row=lower slot="+column);
                return;
            }
            throw new InvalidOperationException("No free factory build menu slot.");
        }
    }
}
