using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using CommonAPI;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;
using crecheng.DSPModSave;
using HarmonyLib;
using xiaoye97;

namespace SprayFactory
{
    [BepInPlugin(Guid, "Spray Factory", "0.1.1")]
    [BepInDependency(CommonAPIPlugin.GUID, "1.6.7")]
    [BepInDependency("me.xiaoye97.plugin.Dyson.LDBTool", "3.0.3")]
    [BepInDependency(DSPModSavePlugin.MODGUID, "1.2.2")]
    [BepInDependency("org.ProfessorCat305.OrbitalRing", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Gnimaerd.DSP.plugin.BuildBarTool", BepInDependency.DependencyFlags.SoftDependency)]
    [CommonAPISubmoduleDependency(nameof(ProtoRegistry), nameof(LocalizationModule))]
    public sealed class Plugin : BaseUnityPlugin, IModCanSave
    {
        public const string Guid = "com.komonad.dsp.sprayfactory";
        internal static ManualLogSource Log;
        private Harmony harmony;
        private void Awake()
        {
            Log = Logger;
            LocalizationModule.RegisterTranslation("SprayFactory.Name", "Spray Factory", "喷涂工厂", "");
            LocalizationModule.RegisterTranslation("SprayFactory.Description", "A belt-fed recipe factory. The upper belt supplies materials; products enter the continuous lower belt. Working power: 300 kW.", "传送带配方工厂。上层传送带供料，产物送入下层贯穿传送带。工作功耗：300 kW。", "");
            LDBTool.PreAddDataAction += Prototypes.Register;
            LDBTool.PostAddDataAction += Prototypes.Finish;
            harmony = new Harmony(Guid);
            harmony.PatchAll();
            Logger.LogInfo("Spray Factory loaded.");
        }
        private void OnDestroy()
        {
            FactoryIcon.Clear();
            RecipeColors.Clear();
            FactoryEffects.Clear();
            FactoryWindow.Hide();
            LDBTool.PreAddDataAction -= Prototypes.Register;
            LDBTool.PostAddDataAction -= Prototypes.Finish;
            harmony?.UnpatchSelf();
        }
        private void LateUpdate()
        {
            if(!Prototypes.Ready) return;
            var factory=GameMain.localPlanet?.factory; var camera=GameCamera.main;
            FactoryEffects.Draw(factory,camera);
        }
        public void Export(BinaryWriter writer) { FactoryStore.Export(writer); }
        public void Import(BinaryReader reader) { FactoryStore.Import(reader); }
        public void IntoOtherSave() { FactoryEffects.Clear(); FactoryStore.Clear(); }
    }
}
