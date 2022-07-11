namespace ValheimVRM
{
    public static class Commands
    {
        public static readonly Console.ConsoleCommand ReloadSettings = new Console.ConsoleCommand(
            "reload_settings",
            "reload VRM settings for your character",
            args =>
            {
                string name = VrmManager.PlayerToName[Player.m_localPlayer];

                if (!VrmManager.VrmDic.ContainsKey(name)) return;
                
                Settings.AddSettingsFromFile(name, VrmManager.VrmDic[name].Source == VRM.SourceType.Shared);
                VrmManager.VrmDic[name].RecalculateSettingsHash();
                
                args.Context.AddString("Settings for " + name + " were reloaded");
                
                Player.m_localPlayer.GetComponent<VrmController>().ShareVrm(false);
            }
        );
        
        public static readonly Console.ConsoleCommand ReloadGlobalSettings = new Console.ConsoleCommand(
            "reload_global_settings",
            "reload global VRM settings",
            args =>
            {
                Settings.ReloadGlobalSettings();
            
                args.Context.AddString("Global settings were reloaded");
            }
        );

        public static int Trigger()
        {
            return 1;
        }
    }
}