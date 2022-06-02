using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ValheimVRM
{
	public static class Settings
	{
        // We should use precompiled type as a container so we could avoid parsing values repeatedly
        public abstract class Container
        {
            public override string ToString()
            {
                return string.Join("\n", from field in GetType().GetFields() select field.Name + "=" + field.GetValue(this));
            }

            public string ToStringDiffOnly()
            {
                var defaults = Activator.CreateInstance(GetType());
                return string.Join("\n", from field in GetType().GetFields() where !field.GetValue(this).Equals(field.GetValue(defaults)) select field.Name + "=" + field.GetValue(this));
            }

            public void LoadFrom(Dictionary<string, string> data)
            {
                var defaults = Activator.CreateInstance(GetType());
                
                List<string> unknownNames = data.Keys.ToList();
                foreach (var field in GetType().GetFields())
                {
                    object value = field.GetValue(defaults);
                    string valueStr;
                    if (data.TryGetValue(field.Name, out valueStr))
                    {
                        try
                        {
                            if (field.FieldType == typeof(float))
                            {
                                value = float.Parse(valueStr);
                            }
                            else if (field.FieldType == typeof(bool))
                            {
                                value = bool.Parse(valueStr);
                            }
                            else if (field.FieldType == typeof(int))
                            {
                                value = int.Parse(valueStr);
                            }
                            else if (field.FieldType == typeof(string))
                            {
                                value = valueStr;
                            }
                            else if (field.FieldType == typeof(Vector3))
                            {
                                var match = new Regex("\\((?<x>[^,]*?),(?<y>[^,]*?),(?<z>[^,]*?)\\)").Match(valueStr);
                                if (match.Success)
                                {
                                    value = new Vector3()
                                    {
                                        x = float.Parse(match.Groups["x"].Value),
                                        y = float.Parse(match.Groups["y"].Value),
                                        z = float.Parse(match.Groups["z"].Value)
                                    };
                                }
                                else
                                {
                                    throw new FormatException();
                                }
                            }
                            else if (field.FieldType == typeof(Vector2))
                            {
                                var match = new Regex("\\((?<x>[^,]*?),(?<y>[^,]*?)\\)").Match(valueStr);
                                if (match.Success)
                                {
                                    value = new Vector2()
                                    {
                                        x = float.Parse(match.Groups["x"].Value),
                                        y = float.Parse(match.Groups["y"].Value)
                                    };
                                }
                                else
                                {
                                    throw new FormatException();
                                }
                            }
                            else if (field.FieldType == typeof(Vector4))
                            {
                                var match = new Regex("\\((?<x>[^,]*?),(?<y>[^,]*?),(?<z>[^,]*?),(?<w>[^,]*?)\\)").Match(valueStr);
                                if (match.Success)
                                {
                                    value = new Vector4()
                                    {
                                        x = float.Parse(match.Groups["x"].Value),
                                        y = float.Parse(match.Groups["y"].Value),
                                        z = float.Parse(match.Groups["z"].Value),
                                        w = float.Parse(match.Groups["w"].Value)
                                    };
                                }
                                else
                                {
                                    throw new FormatException();
                                }
                            }
                            else
                            {
                                Debug.LogWarning("[ValheimVRM] unsupported setting type: " + field.FieldType.FullName + " " + field.Name);
                            }
                        }
                        catch (FormatException ex)
                        {
                            Debug.LogError("[ValheimVRM] failed to read setting: " + field.Name + "=" + valueStr);
                        }

                        unknownNames.Remove(field.Name);
                    }
                    
                    field.SetValue(this, value);
                }

                if (unknownNames.Count > 0)
                {
                    foreach (var name in unknownNames)
                    {
                        Debug.LogWarning("[ValheimVRM] unknown setting: " + name + "=" + data[name]);
                    }
                }
            }

            public void Reset()
            {
                var defaults = Activator.CreateInstance(GetType());
                
                foreach (var field in GetType().GetFields())
                {
                    field.SetValue(this, field.GetValue(defaults));
                }
            }

            public void CopyFrom(Container another)
            {
                foreach (var field in GetType().GetFields())
                {
                    field.SetValue(this, field.GetValue(another));
                }
            }
        }

        public class VrmSettingsContainer : Container
        {
            public float ModelScale = 1.1f;
            public float ModelOffsetY = 0.0f;
            public float PlayerHeight = 1.85f;
            public float PlayerRadius = 0.5f;
            public Vector3 SittingOnChairOffset = Vector3.zero;
            public Vector3 SittingOnThroneOffset = Vector3.zero;
            public Vector3 SittingOnShipOffset = Vector3.zero;
            public Vector3 HoldingMastOffset = Vector3.zero;
            public Vector3 HoldingDragonOffset = Vector3.zero;
            public Vector3 SittingIdleOffset = Vector3.zero;
            public Vector3 SleepingOffset = Vector3.zero;

            public Vector3 RightHandEquipPos = Vector3.zero;
            public Vector3 LeftHandEquipPos = Vector3.zero;
            public Vector3 RightHandBackItemPos = Vector3.zero;
            public Vector3 LeftHandBackItemPos = Vector3.zero;

            public float ModelBrightness = 0.8f;
            public bool FixCameraHeight = true;
            public bool UseMToonShader = false;
            public bool EnablePlayerFade = true;
            public bool AllowShare = true;

            public float SpringBoneStiffness = 1.0f;
            public float SpringBoneGravityPower = 1.0f;

            public float EquipmentScale = 1.0f;
            public float AttackDistanceScale = 1.0f;
            //public float AttackSpeedScale = 1.0f;
            public float MeleeDamageScale = 1.0f;
            public float RangedDamageScale = 1.0f;
            public float InteractionDistanceScale = 1.0f;
            public float SwimDepthScale = 1.0f;
            public float SwimSpeedScale = 1.0f;
            public float BaseHealthScale = 1.0f;
            public float FoodHealthScale = 1.0f;
            public float BaseStaminaScale = 1.0f;
            public float FoodStaminaScale = 1.0f;
            public float WeightLimitScale = 1.0f;
            public float MovementSpeedScale = 1.0f;
            public float JumpForceScale = 1.0f;
        }

        public class GlobalSettingsContainer : Container
        {
            public bool ReloadInMenu = false;
            public bool AllowLocalVRMsForOtherPlayers = true;
        }

        public static string ValheimVRMDir => Path.Combine(Environment.CurrentDirectory, "ValheimVRM");

		public static string PlayerSettingsPath(string playerName) => Path.Combine(ValheimVRMDir, $"settings_{playerName}.txt");
        private static Dictionary<string, VrmSettingsContainer> playerSettings = new Dictionary<string, VrmSettingsContainer>();

        public static readonly GlobalSettingsContainer globalSettings = new GlobalSettingsContainer();

        public static VrmSettingsContainer GetSettings(string playerName)
        {
            return playerSettings.ContainsKey(playerName) ? playerSettings[playerName] : null;
        }

        public static void AddSettingsFromFile(string playerName)
        {
            var path = PlayerSettingsPath(playerName);
            if (File.Exists(path))
            {
                AddSettingsRaw(playerName, File.ReadAllLines(path));
            }
            else
            {
                if (!playerSettings.ContainsKey(playerName)) playerSettings[playerName] = new VrmSettingsContainer();
                else playerSettings[playerName].Reset();
                
                Debug.Log("[ValheimVRM] loaded settings for " + playerName + ":\n" + playerSettings[playerName].ToString());
            }
        }

        public static void AddSettingsRaw(string playerName, ICollection<string> settingLines)
        {
            Dictionary<string, string> settingsData = new Dictionary<string, string>();

            foreach (var setting in ParseSettings(settingLines))
            {
                settingsData[setting.Key] = setting.Value;
            }

            if (!playerSettings.ContainsKey(playerName)) playerSettings[playerName] = new VrmSettingsContainer();
            playerSettings[playerName].LoadFrom(settingsData);

            int maxNameWidth = settingsData.Max(kvp => kvp.Key.Length);
            
            Debug.Log("[ValheimVRM] loaded settings for " + playerName + ":\n" + playerSettings[playerName].ToString());
        }
        
        public static bool ContainsSettings(string playerName)
        {
            return playerSettings.ContainsKey(playerName);
        }
        
        public static IEnumerable<KeyValuePair<string, string>> ParseSettings(IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                int comm_index = line.IndexOf("//");
                var value_line = comm_index > -1 ? line.Substring(0, comm_index) : line;

                int sep_index = value_line.IndexOf("=");
                if (sep_index == -1) continue;

                string key = value_line.Substring(0, sep_index).Trim();
                string value = value_line.Substring(sep_index + 1).Trim();

                yield return new KeyValuePair<string, string>(key, value);
            }
        }

        public static void ReloadGlobalSettings()
        {
            var path = Path.Combine(ValheimVRMDir, "global_settings.txt");

            if (File.Exists(path))
            {
                Dictionary<string, string> settingsData = new Dictionary<string, string>();

                foreach (var setting in ParseSettings(File.ReadAllLines(path)))
                {
                    settingsData[setting.Key] = setting.Value;
                }
                
                globalSettings.LoadFrom(settingsData);
            }
            else
            {
                globalSettings.Reset();
            }
            
            Debug.Log("[ValheimVRM] loaded global settings:\n" + globalSettings.ToString());
        }
    }
}
