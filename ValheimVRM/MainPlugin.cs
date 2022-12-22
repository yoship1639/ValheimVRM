using System.Globalization;
using BepInEx;
using HarmonyLib;
using System.Reflection;

namespace ValheimVRM
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class MainPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.yoship1639.plugins.valheimvrm";
        public const string PluginName = "ValheimVRM";
        public const string PluginVersion = "1.1.3.0";

        void Awake()
        {
            // avoid float parsing error on computers with different cultures
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            
            // we have some global settings to load
            Settings.ReloadGlobalSettings();
            
            // a semi hacky way of loading a default character, no one can name a character with and underscore as far as i am aware. 
            Settings.AddSettingsFromFile("___Default", false);
            
            // Harmonyパッチ作成
            var harmony = new Harmony("com.yoship1639.plugins.valheimvrm.patch");

            // Harmonyパッチ全てを適用する
            harmony.PatchAll();

            // MToonシェーダ初期化
            VRMShaders.Initialize();
        }
    }
}
