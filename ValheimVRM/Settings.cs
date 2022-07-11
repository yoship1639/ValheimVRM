using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using VRM;
using Object = UnityEngine.Object;

namespace ValheimVRM
{
	public static class Settings
	{
        // We should use precompiled type as a container so we could avoid parsing values repeatedly
        public abstract class Container
        {
            public override string ToString()
            {
                return string.Join("\n", from field in GetType().GetFields() where field.GetCustomAttribute(typeof(NonSerializedAttribute)) == null select field.Name + "=" + field.GetValue(this));
            }

            public string ToStringDiffOnly()
            {
                var defaults = Activator.CreateInstance(GetType());
                return string.Join("\n", from field in GetType().GetFields() where !field.GetValue(this).Equals(field.GetValue(defaults)) && field.GetCustomAttribute(typeof(NonSerializedAttribute)) == null select field.Name + "=" + field.GetValue(this));
            }

            public void LoadFrom(Dictionary<string, string> data)
            {
                var defaults = Activator.CreateInstance(GetType());
                Dictionary<string, object> changes = new Dictionary<string, object>();
                
                List<string> unknownNames = data.Keys.ToList();
                foreach (var field in GetType().GetFields())
                {
                    if (field.GetCustomAttribute(typeof(NonSerializedAttribute)) != null) continue;
                    
                    var old = field.GetValue(this);
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
                            else if (field.FieldType.IsEnum)
                            {
                                value = Enum.ToObject(field.FieldType, valueStr);
                            }
                            else
                            {
                                Debug.LogWarning("[ValheimVRM] unsupported setting type: " + field.FieldType.FullName + " " + field.Name);
                            }
                        }
                        catch (Exception)
                        {
                            Debug.LogError("[ValheimVRM] failed to read setting: " + field.Name + "=" + valueStr);
                        }

                        unknownNames.Remove(field.Name);
                    }

                    if (!old.Equals(value)) changes[field.Name] = old;

                    field.SetValue(this, value);
                }

                if (unknownNames.Count > 0)
                {
                    foreach (var name in unknownNames)
                    {
                        Debug.LogWarning("[ValheimVRM] unknown setting: " + name + "=" + data[name]);
                    }
                }

                OnUpdate(changes);
            }

            public void Reset()
            {
                var defaults = Activator.CreateInstance(GetType());
                Dictionary<string, object> changes = new Dictionary<string, object>();
                
                foreach (var field in GetType().GetFields())
                {
                    if (field.GetCustomAttribute(typeof(NonSerializedAttribute)) != null) continue;
                    
                    var old = field.GetValue(this);
                    var value = field.GetValue(defaults);
                    field.SetValue(this, value);
                    if (!old.Equals(value)) changes[field.Name] = old;
                }
                
                OnUpdate(changes);
            }

            public void CopyFrom(Container another)
            {
                Dictionary<string, object> changes = new Dictionary<string, object>();
                
                foreach (var field in GetType().GetFields())
                {
                    if (field.GetCustomAttribute(typeof(NonSerializedAttribute)) != null) continue;
                    
                    var old = field.GetValue(this);
                    var value = field.GetValue(another);
                    field.SetValue(this, value);
                    if (!old.Equals(value)) changes[field.Name] = old;
                }
                
                OnUpdate(changes);
            }

            public virtual void OnUpdate(Dictionary<string, object> oldValues)
            {
                
            }
        }

        public class VrmSettingsContainer : Container
        {
            [NonSerializedAttribute]
            public string Name;
            
            public float ModelScale = 1.1f;
            public float ModelOffsetY = 0.0f;
            public float PlayerHeight = 1.85f;
            public float PlayerRadius = 0.5f;

            public float HeightAspect => PlayerHeight / 1.85f;
            public float RadiusAspect => PlayerRadius / 0.5f;
            
            public Vector3 SittingOnChairOffset = Vector3.zero;
            public Vector3 SittingOnThroneOffset = Vector3.zero;
            public Vector3 SittingOnShipOffset = Vector3.zero;
            public Vector3 HoldingMastOffset = Vector3.zero;
            public Vector3 HoldingDragonOffset = Vector3.zero;
            public Vector3 SittingIdleOffset = Vector3.zero;
            public Vector3 SleepingOffset = Vector3.zero;

            public Vector3 RightHandItemPos = Vector3.zero;
            public Vector3 LeftHandItemPos = Vector3.zero;
            public Vector3 RightHandBackItemPos = Vector3.zero;
            public Vector3 RightHandBackItemToolPos = Vector3.zero;
            public Vector3 LeftHandBackItemPos = Vector3.zero;

            public bool HelmetVisible = false;
            public Vector3 HelmetScale = Vector3.one;
            public Vector3 HelmetOffset = Vector3.zero;

            public bool ChestVisible = false;
            public bool ShouldersVisible = false;
            public bool UtilityVisible = false;
            public bool LegsVisible = false;

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
            public float StealthScale = 1.0f;
            public float DigestionTimeScale = 1.0f;

            public override void OnUpdate(Dictionary<string, object> oldValues)
            {
                if (!VrmManager.VrmDic.ContainsKey(Name)) return;
                
                Player player = null;

                foreach (var p in Player.GetAllPlayers())
                {
                    if (p.GetPlayerName() == Name)
                    {
                        player = p;
                        break;
                    }
                }

                if (player == null) return;

                foreach (var oldValue in oldValues)
                {
                    switch (oldValue.Key)
                    {
                        case nameof(ModelScale):
                            VRM vrm;
                            if (VrmManager.VrmDic.TryGetValue(Name, out vrm))
                            {
                                vrm.VisualModel.transform.localScale = Vector3.one * ModelScale;
                                VrmManager.PlayerToVrmInstance[player].transform.localScale = Vector3.one * ModelScale;
                            }
                            break;
                        
                        case nameof(PlayerHeight):
                            player.GetComponent<CapsuleCollider>().height = PlayerHeight;
                            player.GetComponent<CapsuleCollider>().center = new Vector3(0, PlayerHeight / 2, 0);
                            player.GetComponent<Rigidbody>().centerOfMass = new Vector3(0, PlayerHeight / 2, 0);
                            break;
                        
                        case nameof(PlayerRadius):
                            player.GetComponent<CapsuleCollider>().radius = PlayerRadius;
                            break;
                        
                        case nameof(SpringBoneStiffness):
                            foreach (var bone in player.GetComponent<VrmController>().visual.GetComponentsInChildren<VRMSpringBone>())
                            {
                                bone.m_stiffnessForce = bone.m_stiffnessForce / (float)oldValue.Value * SpringBoneStiffness;
                            }
                            break;
                        
                        case nameof(SpringBoneGravityPower):
                            foreach (var bone in player.GetComponent<VrmController>().visual.GetComponentsInChildren<VRMSpringBone>())
                            {
                                bone.m_gravityPower = bone.m_gravityPower / (float)oldValue.Value * SpringBoneGravityPower;
                            }
                            break;
                        
                        case nameof(InteractionDistanceScale):
                            player.m_maxInteractDistance = player.m_maxInteractDistance / (float)oldValue.Value * InteractionDistanceScale;
                            player.m_maxPlaceDistance = player.m_maxPlaceDistance / (float)oldValue.Value * InteractionDistanceScale;
                            break;

                        case nameof(SwimDepthScale):
                            player.m_swimDepth = player.m_swimDepth / (float)oldValue.Value * SwimDepthScale;
                            break;
    
                        case nameof(SwimSpeedScale):
                            player.m_swimSpeed = player.m_swimSpeed / (float)oldValue.Value * SwimSpeedScale;
                            break;
    
                        case nameof(WeightLimitScale):
                            player.m_maxCarryWeight = player.m_maxCarryWeight / (float)oldValue.Value * WeightLimitScale;
                            break;
    
                        case nameof(MovementSpeedScale):
                            player.m_walkSpeed = player.m_walkSpeed / (float)oldValue.Value * MovementSpeedScale;
                            player.m_runSpeed = player.m_runSpeed / (float)oldValue.Value * MovementSpeedScale;
                            break;

                        case nameof(JumpForceScale):
                            player.m_jumpForce = player.m_jumpForce / (float)oldValue.Value * JumpForceScale;
                            break;
                    }
                }
            }
        }

        public class GlobalSettingsContainer : Container
        {
            public bool ReloadInMenu = false;
            public bool AcceptVrmSharing = true;
            public bool DrawPlayerSizeGizmo = false;
            public float StartVrmShareDelay = 10.0f;
            public bool ForceWindDisabled = false;
            public bool AllowIndividualWinds = true;

            public override void OnUpdate(Dictionary<string, object> oldValues)
            {
                foreach (var oldValue in oldValues)
                {
                    switch (oldValue.Key)
                    {
                        case nameof(DrawPlayerSizeGizmo):
                        {
                            var controller = VrmController.GetLocalController();
                            if (controller != null)
                            {
                                if (DrawPlayerSizeGizmo)
                                {
                                    controller.ActivateSizeGizmo();
                                }
                                else
                                {
                                    controller.DeactivateSizeGizmo();
                                }
                            }
                        }
                            break;
                        case nameof(ForceWindDisabled):
                            foreach (var controller in Object.FindObjectsOfType<VrmController>())
                            {
                                controller.ResetSpringBonesWind();
                            }
                            break;
                        case nameof(AllowIndividualWinds):
                            foreach (var controller in Object.FindObjectsOfType<VrmController>())
                            {
                                controller.ReloadSpringBones();
                            }
                            break;
                    }
                }
            }
        }

        public static string ValheimVRMDir => Path.Combine(Environment.CurrentDirectory, "ValheimVRM");

		public static string PlayerSettingsPath(string playerName, bool shared) => Path.Combine(ValheimVRMDir, shared ? "Shared" : "", $"settings_{playerName}.txt");
        private static Dictionary<string, VrmSettingsContainer> playerSettings = new Dictionary<string, VrmSettingsContainer>();

        public static readonly GlobalSettingsContainer globalSettings = new GlobalSettingsContainer();

        public static VrmSettingsContainer GetSettings(string playerName)
        {
            return playerSettings.ContainsKey(playerName) ? playerSettings[playerName] : null;
        }

        public static void AddSettingsFromFile(string playerName, bool shared)
        {
            var path = PlayerSettingsPath(playerName, shared);
            if (File.Exists(path))
            {
                AddSettingsRaw(playerName, File.ReadAllLines(path));
            }
            else
            {
                if (!playerSettings.ContainsKey(playerName)) playerSettings[playerName] = new VrmSettingsContainer();
                playerSettings[playerName].Name = playerName;
                playerSettings[playerName].Reset();

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
            playerSettings[playerName].Name = playerName;
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

        public static void RemoveSettings(string playerName)
        {
            if (playerSettings.ContainsKey(playerName)) playerSettings.Remove(playerName);
        }
    }
}
