using System;
using System.IO;
using UnityEngine;

namespace ValheimVRM
{
	static class Settings
	{
		public static readonly string ValheimVRMDir = Environment.CurrentDirectory + @"\ValheimVRM";

		public static string PlayerSettingsPath(string playerName) => ValheimVRMDir + $"/settings_{playerName}.txt";

        public static string ReadSettings(string playername, string key)
        {
            string retval = null;

            try
            {
                var settingsPath = PlayerSettingsPath(playername);
                if (!File.Exists(settingsPath)) return null;

                var lines = File.ReadAllLines(settingsPath);
                foreach (var line in lines)
                {
                    try
                    {
                        if (line.Length > 1 && line.Substring(0, 2) == "//") continue;

                        var args = line.Split('=');
                        if (args.Length != 2) continue;

                        if (args[0] == key)
                        {
                            retval = args[1];
                        }
                    }
                    catch { }
                }
            }
            catch { }

            if (retval == null)
            {
                Debug.LogWarning("設定項目が見つかりません: " + key);
            }

            return retval;
        }

        public static int ReadInt(string playername, string key, int defaultValue = 0)
        {
            var str = ReadSettings(playername, key);
            var res = defaultValue;
            if (int.TryParse(str, out res)) return res;
            return defaultValue;
        }

        public static float ReadFloat(string playername, string key, float defaultValue = 0.0f)
        {
            var str = ReadSettings(playername, key);
            var res = defaultValue;
            if (float.TryParse(str, out res)) return res;
            return defaultValue;
        }

        public static bool ReadBool(string playername, string key, bool defaultValue = false)
        {
            var str = ReadSettings(playername, key);
            var res = defaultValue;
            if (bool.TryParse(str, out res)) return res;
            return defaultValue;
        }
    }
}
