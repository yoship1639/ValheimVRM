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
        public const string PluginVersion = "1.1.1.0";

        void Awake()
        {
            // Harmonyパッチ作成
            var harmony = new Harmony("com.yoship1639.plugins.valheimvrm.patch");

            // Harmonyパッチ全てを適用する
            harmony.PatchAll();

            // MToonシェーダ初期化
            VRMShaders.Initialize();
        }
    }
}
