namespace ValheimVRM
{
    public static class Commands
    {
        public static readonly Console.ConsoleCommand ReloadSettings = new Console.ConsoleCommand(
            "reload_settings",
            "reload VRM settings for your character",
            args =>
        {
            Settings.AddSettingsFromFile(VRMModels.PlayerToName[Player.m_localPlayer]);
            
            args.Context.AddString("Settings for " + VRMModels.PlayerToName[Player.m_localPlayer] + " were reloaded");
        });
        
        public static readonly Console.ConsoleCommand ReloadGlobalSettings = new Console.ConsoleCommand(
            "reload_global_settings",
            "reload global VRM settings",
            args =>
            {
                Settings.ReloadGlobalSettings();
            
                args.Context.AddString("Global settings were reloaded");
            });

        public static int Trigger()
        {
            return 1;
        }
    }
}