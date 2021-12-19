using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ValheimVRM
{
	static class Settings
	{
		public static string ValheimVRMDir => Path.Combine(Environment.CurrentDirectory, "ValheimVRM");

		public static string PlayerSettingsPath(string playerName) => Path.Combine(ValheimVRMDir, $"settings_{playerName}.txt");
        private static Dictionary<string, string[]> playerSettings = new Dictionary<string, string[]>();

        public static void AddSettings(string playerName, string[] settings)
        {
            playerSettings[playerName] = settings;
        }

        public static string[] GetSettings(string playerName)
        {
            return playerSettings.ContainsKey(playerName) ? playerSettings[playerName] : null;
        }

        public static bool AddSettingsFromFile(string playerName)
        {
            var path = PlayerSettingsPath(playerName);
            if (File.Exists(path))
            {
                playerSettings[playerName] = File.ReadAllLines(path);
                return true;
            }
            return false;
        }

        public static bool ContainsSettings(string playerName)
        {
            return playerSettings.ContainsKey(playerName);
        }

        public static string ReadSettings(string playername, string key)
        {
            if (!playerSettings.ContainsKey(playername)) return null;

            string retval = null;

            try
            {
                var lines = playerSettings[playername];
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
                Debug.LogWarning("[ValheimVRM] 設定項目が見つかりません: " + key);
            }

            return retval;
        }

        public static int ReadInt(string playername, string key, int defaultValue = 0, bool debugLog = true)
        {
            var str = ReadSettings(playername, key);
            var res = defaultValue;
            if (int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out res))
            {
                if (debugLog) Debug.Log("[ValheimVRM] " + key + ": " + res);
                return res;
            } 
            return defaultValue;
        }

        public static float ReadFloat(string playername, string key, float defaultValue = 0.0f, bool debugLog = true)
        {
            var str = ReadSettings(playername, key);
            var res = defaultValue;
            if (float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out res))
            {
                if (debugLog) Debug.Log("[ValheimVRM] " + key + ": " + res);
                return res;
            }
            return defaultValue;
        }

        public static bool ReadBool(string playername, string key, bool defaultValue = false, bool debugLog = true)
        {
            var str = ReadSettings(playername, key);
            var res = defaultValue;
            if (bool.TryParse(str, out res))
            {
                if (debugLog) Debug.Log("[ValheimVRM] " + key + ": " + res);
                return res;
            }
            return defaultValue;
        }

        public static Vector3 ReadVector3(string playername, string key, Vector3 defaultValue = default, bool debugLog = true)
        {
            var str = ReadSettings(playername, key);
            if (str == null) return defaultValue;
            var match = new Regex("\\((?<x>[^,]*?),(?<y>[^,]*?),(?<z>[^,]*?)\\)").Match(str);
            if (match.Success == false) return defaultValue;
            try
            {
                var res = new Vector3()
                {
                    x = float.Parse(match.Groups["x"].Value),
                    y = float.Parse(match.Groups["y"].Value),
                    z = float.Parse(match.Groups["z"].Value)
                };
                if (debugLog) Debug.Log("[ValheimVRM] " + key + ": " + res);
                return res;
            }
            catch (FormatException ex)
            {
                Debug.LogError(ex);
                return defaultValue;
            }
        }
    }
}
