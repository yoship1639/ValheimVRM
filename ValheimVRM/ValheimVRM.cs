using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UniGLTF;
using UnityEngine;
using VRM;
using Object = UnityEngine.Object;

namespace ValheimVRM
{
	[HarmonyPatch(typeof(Shader))]
	[HarmonyPatch(nameof(Shader.Find))]
	static class ShaderPatch
	{
		static bool Prefix(ref Shader __result, string name)
		{
			if (VRMShaders.Shaders.TryGetValue(name, out var shader))
			{
				__result = shader;
				return false;
			}

			return true;
		}
	}

	public static class VRMShaders
	{
		public static Dictionary<string, Shader> Shaders { get; } = new Dictionary<string, Shader>();

		public static void Initialize()
		{
			var bundlePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"ValheimVRM.shaders");
			if (File.Exists(bundlePath))
			{
				var assetBundle = AssetBundle.LoadFromFile(bundlePath);
				var assets = assetBundle.LoadAllAssets<Shader>();
				foreach (var asset in assets)
				{
					UnityEngine.Debug.Log("[ValheimVRM] Add Shader: " + asset.name);
					Shaders.Add(asset.name, asset);
				}
			}
		}
	}

	public class VRM
	{
		public GameObject VisualModel { get; private set; }
		public byte[] SrcBytes;
		public byte[] SrcBytesHash;
		public byte[] SettingsHash;
		public string Name { get; private set; }
		public bool IsShared = false;

		public VRM(GameObject visualModel, string name)
		{
			VisualModel = visualModel;
			Name = name;
		}

		~VRM()
		{
			if (VisualModel != null)
			{
				Object.Destroy(VisualModel);
			}
		}

		public void RecalculateSrcBytesHash()
		{
			using (var md5 = System.Security.Cryptography.MD5.Create())
			{
				md5.TransformFinalBlock(SrcBytes, 0, SrcBytes.Length);
				SrcBytesHash = md5.Hash;
			}
		}

		public void RecalculateSettingsHash()
		{
			using (var md5 = System.Security.Cryptography.MD5.Create())
			{
				byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(string.Join("\n", Settings.GetSettings(Name).ToStringDiffOnly()));
				SettingsHash = md5.ComputeHash(inputBytes);
			}
		}
	}

	public static class VRMModels
	{
		public static Dictionary<Player, GameObject> PlayerToVrmInstance = new Dictionary<Player, GameObject>();
		public static Dictionary<Player, string> PlayerToName = new Dictionary<Player, string>();
		public static Dictionary<string, VRM> VrmDic = new Dictionary<string, VRM>();
		
		public static VRM RegisterVrm(VRM vrm, LODGroup sampleLODGroup)
		{
			if (vrm.VisualModel == null) return null;
			
			foreach (var registered in VrmDic)
			{
				if (registered.Key == vrm.Name) continue;

				if (registered.Value == vrm || registered.Value.VisualModel == vrm.VisualModel)
				{
					Debug.LogError("[ValheimVRM] attempt to register a vrm that is already registered as " + registered.Key);
					return null;
				}
			}
			
			if (VrmDic.ContainsKey(vrm.Name))
			{
				var existing = VrmDic[vrm.Name];
				
				if (existing == vrm) return vrm;
				
				if (existing.VisualModel != vrm.VisualModel)
				{
					Object.Destroy(existing.VisualModel);
				}

				VrmDic[vrm.Name] = null;
			}

			Object.DontDestroyOnLoad(vrm.VisualModel);
			
			VrmDic[vrm.Name] = vrm;

			//[Error: Unity Log] _Cutoff: Range
			//[Error: Unity Log] _MainTex: Texture
			//[Error: Unity Log] _SkinBumpMap: Texture
			//[Error: Unity Log] _SkinColor: Color
			//[Error: Unity Log] _ChestTex: Texture
			//[Error: Unity Log] _ChestBumpMap: Texture
			//[Error: Unity Log] _ChestMetal: Texture
			//[Error: Unity Log] _LegsTex: Texture
			//[Error: Unity Log] _LegsBumpMap: Texture
			//[Error: Unity Log] _LegsMetal: Texture
			//[Error: Unity Log] _BumpScale: Float
			//[Error: Unity Log] _Glossiness: Range
			//[Error: Unity Log] _MetalGlossiness: Range

			// Shader replacement
			var brightness = Settings.GetSettings(vrm.Name).ModelBrightness;
			var materials = new List<Material>();
			foreach (var smr in vrm.VisualModel.GetComponentsInChildren<SkinnedMeshRenderer>())
			{
				foreach (var mat in smr.materials)
				{
					if (!materials.Contains(mat)) materials.Add(mat);
				}
			}
			foreach (var mr in vrm.VisualModel.GetComponentsInChildren<MeshRenderer>())
			{
				foreach (var mat in mr.materials)
				{
					if (!materials.Contains(mat)) materials.Add(mat);
				}
			}

			if (Settings.GetSettings(vrm.Name).UseMToonShader)
			{
				foreach (var mat in materials)
				{
					if (mat.HasProperty("_Color"))
					{
						var color = mat.GetColor("_Color");
						color.r *= brightness;
						color.g *= brightness;
						color.b *= brightness;
						mat.SetColor("_Color", color);
					}
				}
			}
			else
			{
				var shader = Shader.Find("Custom/Player");
				foreach (var mat in materials)
				{
					if (mat.shader == shader) continue;

					var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

					var mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") as Texture2D : null;
					Texture2D tex = mainTex;
					if (mainTex != null)
					{
						tex = new Texture2D(mainTex.width, mainTex.height);
						var colors = mainTex.GetPixels();
						for (var i = 0; i < colors.Length; i++)
						{
							var col = colors[i] * color;
							float h, s, v;
							Color.RGBToHSV(col, out h, out s, out v);
							v *= brightness;
							colors[i] = Color.HSVToRGB(h, s, v);
							colors[i].a = col.a;
						}
						tex.SetPixels(colors);
						tex.Apply();
					}

					var bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
					mat.shader = shader;

					mat.SetTexture("_MainTex", tex);
					mat.SetTexture("_SkinBumpMap", bumpMap);
					mat.SetColor("_SkinColor", color);
					mat.SetTexture("_ChestTex", tex);
					mat.SetTexture("_ChestBumpMap", bumpMap);
					mat.SetTexture("_LegsTex", tex);
					mat.SetTexture("_LegsBumpMap", bumpMap);
					mat.SetFloat("_Glossiness", 0.2f);
					mat.SetFloat("_MetalGlossiness", 0.0f);
					
				}
			}

			var lodGroup = vrm.VisualModel.AddComponent<LODGroup>();
			if (Settings.GetSettings(vrm.Name).EnablePlayerFade)
			{
				lodGroup.SetLODs(new LOD[]
				{
					new LOD(0.1f, vrm.VisualModel.GetComponentsInChildren<SkinnedMeshRenderer>())
				});
			}
			lodGroup.RecalculateBounds();
			
			lodGroup.fadeMode = sampleLODGroup.fadeMode;
			lodGroup.animateCrossFading = sampleLODGroup.animateCrossFading;

			vrm.VisualModel.SetActive(false);

			return vrm;
		}
	}

	[HarmonyPatch(typeof(VisEquipment), "UpdateLodgroup")]
	static class Patch_VisEquipment_UpdateLodgroup
	{
		[HarmonyPostfix]
		static void Postfix(VisEquipment __instance)
		{
			if (!__instance.m_isPlayer) return;
			var player = __instance.GetComponent<Player>();
			if (player == null || !VRMModels.PlayerToVrmInstance.ContainsKey(player)) return;

			var hair = __instance.GetField<VisEquipment, GameObject>("m_hairItemInstance");
			if (hair != null) SetVisible(hair, false);

			var beard = __instance.GetField<VisEquipment, GameObject>("m_beardItemInstance");
			if (beard != null) SetVisible(beard, false);

			var chestList = __instance.GetField<VisEquipment, List<GameObject>>("m_chestItemInstances");
			if (chestList != null) foreach (var chest in chestList) SetVisible(chest, false);

			var legList = __instance.GetField<VisEquipment, List<GameObject>>("m_legItemInstances");
			if (legList != null) foreach (var leg in legList) SetVisible(leg, false);

			var shoulderList = __instance.GetField<VisEquipment, List<GameObject>>("m_shoulderItemInstances");
			if (shoulderList != null) foreach (var shoulder in shoulderList) SetVisible(shoulder, false);

			var utilityList = __instance.GetField<VisEquipment, List<GameObject>>("m_utilityItemInstances");
			if (utilityList != null) foreach (var utility in utilityList) SetVisible(utility, false);

			var helmet = __instance.GetField<VisEquipment, GameObject>("m_helmetItemInstance");
			if (helmet != null) SetVisible(helmet, false);

			// 武器位置合わせ
			var name = VRMModels.PlayerToName[player];

			var settings = Settings.GetSettings(name);
			float equipmentScale = settings.EquipmentScale;
			Vector3 equipmentScaleVector = new Vector3(equipmentScale, equipmentScale, equipmentScale);
			
			var leftItem = __instance.GetField<VisEquipment, GameObject>("m_leftItemInstance");
			if (leftItem != null)
			{
				leftItem.transform.localPosition = settings.LeftHandEquipPos;
				leftItem.transform.localScale = equipmentScaleVector;
			}

			var rightItem = __instance.GetField<VisEquipment, GameObject>("m_rightItemInstance");
			if (rightItem != null)
			{
				rightItem.transform.localPosition = settings.RightHandEquipPos;
				rightItem.transform.localScale = equipmentScaleVector;
			}
			
			// divided  by 100 to keep the settings file positions in the same number range. (position offset appears to be on the world, not local)
			var rightBackItem = __instance.GetField<VisEquipment, GameObject>("m_rightBackItemInstance");
			if (rightBackItem != null)
			{
				rightBackItem.transform.localPosition = settings.RightHandBackItemPos / 100.0f;
				rightBackItem.transform.localScale = equipmentScaleVector / 100.0f;
			}
			
			var leftBackItem = __instance.GetField<VisEquipment, GameObject>("m_leftBackItemInstance");
			if (leftBackItem != null)
			{
				leftBackItem.transform.localPosition = settings.LeftHandBackItemPos / 100.0f;
				leftBackItem.transform.localScale = equipmentScaleVector / 100.0f;
			}
		}

		private static void SetVisible(GameObject obj, bool flag)
		{
			foreach (var mr in obj.GetComponentsInChildren<MeshRenderer>()) mr.enabled = flag;
			foreach (var smr in obj.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.enabled = flag;
		}
	}

	[HarmonyPatch(typeof(Humanoid), "OnRagdollCreated")]
	static class Patch_Humanoid_OnRagdollCreated
	{
		[HarmonyPostfix]
		static void Postfix(Humanoid __instance, Ragdoll ragdoll)
		{
			if (__instance is Player player)
			{
				foreach (var smr in ragdoll.GetComponentsInChildren<SkinnedMeshRenderer>())
				{
					smr.forceRenderingOff = true;
					smr.updateWhenOffscreen = true;
				}
			

				var ragAnim = ragdoll.gameObject.AddComponent<Animator>();
				ragAnim.keepAnimatorControllerStateOnDisable = true;
				ragAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

				var orgAnim = (player.GetField<Player, Animator>("m_animator"));
				ragAnim.avatar = orgAnim.avatar;

				if (VRMModels.PlayerToVrmInstance.TryGetValue(player, out var vrm))
				{
					vrm.transform.SetParent(ragdoll.transform);
					vrm.GetComponent<VRMAnimationSync>().Setup(ragAnim, Settings.GetSettings(VRMModels.PlayerToName[player]), true);
				}
			}
		}
	}

	[HarmonyPatch(typeof(Character), "SetVisible")]
	static class Patch_Character_SetVisible
	{
		[HarmonyPostfix]
		static void Postfix(Character __instance, bool visible)
		{
			if (!__instance.IsPlayer()) return;

			if (VRMModels.PlayerToVrmInstance.TryGetValue((Player)__instance, out var vrm))
			{
				var lodGroup = vrm.GetComponent<LODGroup>();
				if (visible)
				{
					lodGroup.localReferencePoint = __instance.GetField<Character, Vector3>("m_originalLocalRef");
				}
				else
				{
					lodGroup.localReferencePoint = new Vector3(999999f, 999999f, 999999f);
				}
			}
		}
	}

	[HarmonyPatch(typeof(Player), "OnDeath")]
	static class Patch_Player_OnDeath
	{
		[HarmonyPostfix]
		static void Postfix(Player __instance)
		{
			string name = null;
			if (VRMModels.PlayerToName.ContainsKey(__instance)) name = VRMModels.PlayerToName[__instance];
			if (name != null && Settings.GetSettings(name).FixCameraHeight)
			{
				GameObject.Destroy(__instance.GetComponent<VRMEyePositionSync>());
			}
		}
	}

	[HarmonyPatch(typeof(Character), "GetHeadPoint")]
	static class Patch_Character_GetHeadPoint
	{
		[HarmonyPostfix]
		static bool Prefix(Character __instance, ref Vector3 __result)
		{
			var player = __instance as Player;
			if (player == null) return true;

			if (VRMModels.PlayerToVrmInstance.TryGetValue(player, out var vrm))
			{
				var animator = vrm.GetComponentInChildren<Animator>();
				if (animator == null) return true;

				var head = animator.GetBoneTransform(HumanBodyBones.Head);
				if (head == null) return true;

				__result = head.position;
				return false;
			}
			
			return true;
		}
	}

	[HarmonyPatch(typeof(Humanoid), "StartAttack")]
	static class Patch_Humanoid_StartAttack
	{
		[HarmonyPostfix]
		static void Postfix(Humanoid __instance, Character target, bool secondaryAttack)
		{
			if (__instance is Player player)
			{
				ref Attack attack = ref AccessTools.FieldRefAccess<Player, Attack>("m_currentAttack").Invoke(player);

				ref float time = ref AccessTools.FieldRefAccess<Attack, float>("m_time").Invoke(attack);
				if (time != 0) return;
				
				string playerName;
				if (VRMModels.PlayerToName.TryGetValue(player, out playerName))
				{
					if (Settings.ContainsSettings(playerName))
					{
						var settings = Settings.GetSettings(playerName);
						attack.m_attackRange *= settings.AttackDistanceScale;
						attack.m_attackHeight *= settings.PlayerHeight / 1.85f;
						attack.m_attackOffset *= settings.PlayerHeight / 1.85f;
						
						//var anim = attack.GetField<Attack, ZSyncAnimation>("m_zanim");
						//var nview = anim.GetField<ZSyncAnimation, ZNetView>("m_nview");
						//ref var animSpeedID = ref AccessTools.StaticFieldRefAccess<ZSyncAnimation, int>("m_animSpeedID");
						//nview.GetZDO().Set(animSpeedID, nview.GetZDO().GetFloat(animSpeedID) * settings.AttackSpeedScale);
						//var animator = anim.GetField<ZSyncAnimation, Animator>("m_animator");
						//animator.speed *= settings.AttackSpeedScale;

						switch (attack.m_attackType)
						{
							case Attack.AttackType.Area:
							case Attack.AttackType.Horizontal:
							case Attack.AttackType.Vertical:
								attack.m_damageMultiplier *= settings.MeleeDamageScale;
								break;
							
							case Attack.AttackType.Projectile:
							case Attack.AttackType.TriggerProjectile:
								attack.m_damageMultiplier *= settings.RangedDamageScale;
								break;
						}
					}
				}
			}
		}
	}

	[HarmonyPatch(typeof(Player), "GetTotalFoodValue")]
	static class Patch_Player_GetTotalFoodValue
	{
		[HarmonyPostfix]
		static void Postfix(Player __instance, out float hp, out float stamina)
		{
			float baseHealthScale = 1f;
			float foodHealthScale = 1f;
			float baseStaminaScale = 1f;
			float foodStaminaScale = 1f;
			
			string playerName;
			if (VRMModels.PlayerToName.TryGetValue(__instance, out playerName))
			{
				var settings = Settings.GetSettings(playerName);
				baseHealthScale = settings.BaseHealthScale;
				foodHealthScale = settings.FoodHealthScale;
				baseStaminaScale = settings.BaseStaminaScale;
				foodStaminaScale = settings.FoodStaminaScale;
			}
			
			hp = __instance.m_baseHP * baseHealthScale;
			stamina = __instance.m_baseStamina * baseStaminaScale;
			foreach (Player.Food food in __instance.GetFoods())
			{
				hp += food.m_health * foodHealthScale;
				stamina += food.m_stamina * foodStaminaScale;
			}
		}
	}

	[HarmonyPatch(typeof(Player), "Start")]
	static class Patch_Player_Start
	{
		const int MaxPacketSize = 1024 * 100;
		
		class SharedVRMLoadingProcess
		{
			public bool UseExistingData = false;
			public List<byte[]> Packets = new List<byte[]>();
			public bool PacketsDone = false;
			
			public bool UseExistingSettings = false;
			public string Settings = null;
			public bool SettingsDone = false;

			public bool IsLoaded() => (UseExistingData ? true : PacketsDone) && (UseExistingSettings ? true : SettingsDone);

			public byte[] GetVRMData()
			{
				int size = 0;
				foreach (var packet in Packets)
				{
					size += packet.Length;
				}

				byte[] bytes = new byte[size];

				int s = 0;
				foreach (var packet in Packets)
				{
					Array.Copy(packet, 0, bytes, s, packet.Length);
					s += packet.Length;
				}

				return bytes;
			}
		}

		private static Dictionary<long, SharedVRMLoadingProcess> sharedVRMLoadingProcesses = new Dictionary<long, SharedVRMLoadingProcess>();

		[HarmonyPostfix]
		static void Postfix(Player __instance)
		{
			Commands.Trigger();

			string playerName = null;
			string localPlayerName = null;
			if (Game.instance != null)
			{
				localPlayerName = Game.instance.GetPlayerProfile().GetName();
				playerName = __instance.GetPlayerName();
				if (playerName == "" || playerName == "...") playerName = localPlayerName;
			}
			else
			{
				var index = FejdStartup.instance.GetField<FejdStartup, int>("m_profileIndex");
				var profiles = FejdStartup.instance.GetField<FejdStartup, List<PlayerProfile>>("m_profiles");
				if (index >= 0 && index < profiles.Count) playerName = profiles[index].GetName();
				localPlayerName = playerName;
			}
			
			Settings.VrmSettingsContainer settings = null;

			bool isInMenu = __instance.gameObject.scene.name == "start";

			if (isInMenu)
			{
				foreach (var name in VRMModels.VrmDic.Keys)
				{
					var vrm = VRMModels.VrmDic[name];
					if (vrm.IsShared)
					{
						VRMModels.VrmDic.Remove(name);
					}
				}
				
				VRMModels.PlayerToName.Clear();
				VRMModels.PlayerToVrmInstance.Clear();

				sharedVRMLoadingProcesses.Clear();
			}

			// we have to use it in lambdas anyways, so no sense to make it ref
			var nview = AccessTools.FieldRefAccess<Player, ZNetView>("m_nview").Invoke(__instance);
			bool online = nview.GetZDO() != null;

			const string fc_hash = "VRM_Hash";                     // send hashes
			const string fc_data_query = "VRM_Data_Query";         // request data
			const string fc_data = "VRM_Data";                     // send data packet
			const string fc_data_cb = "VRM_Datta_Callback";        // request next data packet
			const string fc_settings_query = "VRM_Settings_Query"; // request settings
			const string fc_settings = "VRM_Settings";             // send settings

			Action<long, int> sendVrmDataPacket = (target, packetIndex) =>
			{
				byte[] vrmBytes = VRMModels.VrmDic[localPlayerName].SrcBytes;
				int packetCount = Mathf.CeilToInt((float)vrmBytes.Length / MaxPacketSize);

				if (packetIndex >= packetCount)
				{
					nview.InvokeRPC(target, fc_data, packetIndex, packetCount, new ZPackage());
					return;
				}
					
				int packetSize = packetIndex < packetCount - 1
					? MaxPacketSize
					: vrmBytes.Length % packetCount;
				byte[] packetData = new byte[packetSize];
				Array.Copy(vrmBytes, packetIndex * MaxPacketSize, packetData, 0, packetSize);

				nview.InvokeRPC(target, fc_data, packetIndex, packetCount, new ZPackage(packetData));
							
				Debug.Log("[ValheimVRM] sent packet " + (packetIndex + 1) + " of " + packetCount + " to " + (target == ZNetView.Everybody ? "everyone" : target.ToString()));
			};
				
			Action<long> ShareVRM = (target) =>
			{
				// We assume that our VRM always have SrcBytes set
				if (settings.AllowShare && VRMModels.VrmDic.ContainsKey(localPlayerName))
				{
					Debug.Log("[ValheimVRM] sharing " + localPlayerName + " vrm");

					var vrm = VRMModels.VrmDic[localPlayerName];
					
					nview.InvokeRPC(ZNetView.Everybody, fc_hash, localPlayerName, new ZPackage(vrm.SrcBytesHash), new ZPackage(vrm.SettingsHash));

				}
			};

			if (online)
			{
				nview.Register(fc_hash, (long sender, string name, ZPackage dataHash, ZPackage settingsHash) =>
				{
					if (nview.GetZDO().m_owner == sender) return;
					
					Debug.Log("[ValheimVRM] got vrm hashes from " + playerName + ": " + dataHash.GetArray().GetHaxadecimalString() + " " + settingsHash.GetArray().GetHaxadecimalString());
					
					if (VRMModels.VrmDic.ContainsKey(name))
					{
						var vrm = VRMModels.VrmDic[name];
						var process = new SharedVRMLoadingProcess();
						bool required = false;
						
						if (!Enumerable.SequenceEqual(vrm.SrcBytesHash, dataHash.GetArray()))
						{
							nview.InvokeRPC(fc_data_query);
							required = true;
						}
						else
						{
							process.UseExistingData = true;
						}

						if (!Enumerable.SequenceEqual(vrm.SettingsHash, settingsHash.GetArray()))
						{
							nview.InvokeRPC(fc_settings_query);
							required = true;
						}
						else
						{
							process.UseExistingSettings = true;
						}

						if (required)
						{
							sharedVRMLoadingProcesses.Add(sender, process);
						}
					}
					else
					{
						nview.InvokeRPC(fc_data_query);
						nview.InvokeRPC(fc_settings_query);
					}
				});
				
				nview.Register(fc_data_query, sender =>
				{
					if (nview.GetZDO().m_owner == sender) return;
					
					sendVrmDataPacket(sender, 0);
				});

				nview.Register(fc_data, (long sender, int packetIndex, int totalCount, ZPackage packagedBytes) =>
				{
					if (nview.GetZDO().m_owner == sender) return;
					
					if (!sharedVRMLoadingProcesses.ContainsKey(sender)) sharedVRMLoadingProcesses.Add(sender, new SharedVRMLoadingProcess());
					
					var vrm = sharedVRMLoadingProcesses[sender];

					var bytes = packagedBytes.ReadByteArray();

					if (bytes.Length == 0)
					{
						vrm.PacketsDone = true;
						Debug.Log("[ValheimVRM] received all vrm data packets from " + sender);
					}
					else
					{
						vrm.Packets.Add(packagedBytes.ReadByteArray());
						Debug.Log("[ValheimVRM] received vrm data packet " + packetIndex + " of " + totalCount + " from " + sender);
					}

					nview.InvokeRPC(sender, fc_data_cb, packetIndex);
					
					if (vrm.IsLoaded()) SharedVRMLoaded(sender);
				});
				
				nview.Register(fc_data_cb, (long sender, int packetIndex) =>
				{
					sendVrmDataPacket(sender, packetIndex + 1);
				});
				
				nview.Register(fc_settings_query, sender =>
				{
					if (nview.GetZDO().m_owner == sender) return;
					
					nview.InvokeRPC(sender, fc_settings, string.Join("\n", Settings.GetSettings(localPlayerName).ToStringDiffOnly()));
				});
				
				nview.Register(fc_settings, (long sender, string settingsString) =>
				{
					if (nview.GetZDO().m_owner == sender) return;
					
					if (!sharedVRMLoadingProcesses.ContainsKey(sender)) sharedVRMLoadingProcesses.Add(sender, new SharedVRMLoadingProcess());

					var vrm = sharedVRMLoadingProcesses[sender];
					
					vrm.Settings = settingsString;
					vrm.SettingsDone = true;
					
					Debug.Log("[ValheimVRM] received vrm settings from " + sender);
					
					if (vrm.IsLoaded()) SharedVRMLoaded(sender);
				});
			}

			if (!string.IsNullOrEmpty(playerName))
			{
				// If owner of current player or offline
				if (Settings.globalSettings.AllowLocalVRMsForOtherPlayers ?  true : (online && nview.IsOwner()) || !online)
				{
					VRM vrm = null;

					if (!Settings.ContainsSettings(playerName) || Settings.globalSettings.ReloadInMenu && isInMenu)
					{
						// We allow to have no file at all, we just use default parameters
						Settings.AddSettingsFromFile(playerName);
					}

					settings = Settings.GetSettings(playerName);
					
					var scale = settings.ModelScale;

					if (!VRMModels.VrmDic.ContainsKey(playerName) || Settings.globalSettings.ReloadInMenu && isInMenu)
					{
						var path = Path.Combine(Environment.CurrentDirectory, "ValheimVRM", $"{playerName}.vrm");
						
						if (File.Exists(path))
						{
							var vrmVisual = ImportVRM(path, scale);
							if (vrmVisual != null)
							{
								vrm = new VRM(vrmVisual, playerName);
								vrm = VRMModels.RegisterVrm(vrm, __instance.GetComponentInChildren<LODGroup>());
								if (vrm != null)
								{
									vrm.SrcBytes = File.ReadAllBytes(path);
									vrm.RecalculateSrcBytesHash();
								}
							}
						}
						else
						{
							Debug.LogError("[ValheimVRM] VRM file not found: " + path);
						}
					}
					else
					{
						vrm = VRMModels.VrmDic[playerName];
					}

					if (Settings.globalSettings.ReloadInMenu && isInMenu && vrm != null)
					{
						vrm.RecalculateSettingsHash();
					}

					if (online && nview.IsOwner())
					{
						nview.InvokeRPC(ZNetView.Everybody, fc_data_query);
						nview.InvokeRPC(ZNetView.Everybody, fc_settings_query);
					}

					if (vrm != null)
					{
						if (online && nview.IsOwner())
						{
							ShareVRM(ZNetView.Everybody);
						}

						SetVrmToPlayer(__instance, playerName);
					}
				}
			}
		}

		private static GameObject ImportVRM(string path, float scale)
		{
			try
			{
				// 1. GltfParser を呼び出します。
				//    GltfParser はファイルから JSON 情報とバイナリデータを読み出します。
				var parser = new GltfParser();
				parser.ParsePath(path);

				// 2. GltfParser のインスタンスを引数にして VRMImporterContext を作成します。
				//    VRMImporterContext は VRM のロードを実際に行うクラスです。
				using (var context = new VRMImporterContext(parser))
				{
					// 3. Load 関数を呼び出し、VRM の GameObject を生成します。
					context.Load();

					// 4. （任意） SkinnedMeshRenderer の UpdateWhenOffscreen を有効にできる便利関数です。
					context.EnableUpdateWhenOffscreen();

					// 5. VRM モデルを表示します。
					context.ShowMeshes();

					// 6. VRM の GameObject が実際に使用している UnityEngine.Object リソースの寿命を VRM の GameObject に紐付けます。
					//    つまり VRM の GameObject の破棄時に、実際に使用しているリソース (Texture, Material, Mesh, etc) をまとめて破棄することができます。
					context.DisposeOnGameObjectDestroyed();

					context.Root.transform.localScale *= scale;

					Debug.Log("[ValheimVRM] VRM read successful");
					Debug.Log("[ValheimVRM] vrm file path: " + path);

					// 7. Root の GameObject を return します。
					//    Root の GameObject とは VRMMeta コンポーネントが付与されている GameObject のことです。
					return context.Root;
				}
			}
			catch (Exception ex)
			{
				Debug.LogError(ex);
			}

			return null;
		}

		private static GameObject ImportVRM(byte[] buf, float scale)
		{
			try
			{
				// 1. GltfParser を呼び出します。
				//    GltfParser はファイルから JSON 情報とバイナリデータを読み出します。
				var parser = new GltfParser();
				parser.ParseGlb(buf);

				// 2. GltfParser のインスタンスを引数にして VRMImporterContext を作成します。
				//    VRMImporterContext は VRM のロードを実際に行うクラスです。
				using (var context = new VRMImporterContext(parser))
				{
					// 3. Load 関数を呼び出し、VRM の GameObject を生成します。
					context.Load();

					// 4. （任意） SkinnedMeshRenderer の UpdateWhenOffscreen を有効にできる便利関数です。
					context.EnableUpdateWhenOffscreen();

					// 5. VRM モデルを表示します。
					context.ShowMeshes();

					// 6. VRM の GameObject が実際に使用している UnityEngine.Object リソースの寿命を VRM の GameObject に紐付けます。
					//    つまり VRM の GameObject の破棄時に、実際に使用しているリソース (Texture, Material, Mesh, etc) をまとめて破棄することができます。
					context.DisposeOnGameObjectDestroyed();

					context.Root.transform.localScale *= scale;

					Debug.Log("[ValheimVRM] VRM read successful");

					// 7. Root の GameObject を return します。
					//    Root の GameObject とは VRMMeta コンポーネントが付与されている GameObject のことです。
					return context.Root;
				}
			}
			catch (Exception ex)
			{
				Debug.LogError(ex);
			}

			return null;
		}

		private static void SetVrmToPlayer(Player player, string vrmName)
		{
			var settings = Settings.GetSettings(vrmName);
			
			player.m_maxInteractDistance *= settings.InteractionDistanceScale;
			player.m_maxPlaceDistance *= settings.InteractionDistanceScale;
			player.m_swimDepth *= settings.SwimDepthScale;
			player.m_swimSpeed *= settings.SwimSpeedScale;
			player.m_maxCarryWeight *= settings.WeightLimitScale;
			player.m_walkSpeed *= settings.MovementSpeedScale;
			player.m_runSpeed *= settings.MovementSpeedScale;
			player.m_jumpForce *= settings.JumpForceScale;
			
			var vrmModel = Object.Instantiate(VRMModels.VrmDic[vrmName].VisualModel);
			VRMModels.PlayerToVrmInstance[player] = vrmModel;
			VRMModels.PlayerToName[player] = vrmName;
			vrmModel.SetActive(true);
			vrmModel.transform.SetParent(player.GetComponentInChildren<Animator>().transform.parent, false);

			float newHeight = settings.PlayerHeight;
			float newRadius = settings.PlayerRadius;

			var collider = player.gameObject.GetComponent<CapsuleCollider>();
			collider.height = newHeight;
			collider.radius = newRadius;
			collider.center = new Vector3(0, newHeight / 2, 0);

			player.GetComponent<Rigidbody>().centerOfMass = collider.center;

			foreach (var smr in player.GetVisual().GetComponentsInChildren<SkinnedMeshRenderer>())
			{
				smr.forceRenderingOff = true;
				smr.updateWhenOffscreen = true;
			}

			var orgAnim = AccessTools.FieldRefAccess<Player, Animator>(player, "m_animator");
			orgAnim.keepAnimatorControllerStateOnDisable = true;
			orgAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

			vrmModel.transform.localPosition = orgAnim.transform.localPosition;

			// アニメーション同期
			if (vrmModel.GetComponent<VRMAnimationSync>() == null) vrmModel.AddComponent<VRMAnimationSync>().Setup(orgAnim, settings, false);
			else vrmModel.GetComponent<VRMAnimationSync>().Setup(orgAnim, settings, false);

			// カメラ位置調整
			if (settings.FixCameraHeight)
			{
				var vrmEye = vrmModel.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftEye);
				if (vrmEye == null) vrmEye = vrmModel.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
				if (vrmEye == null) vrmEye = vrmModel.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Neck);
				if (vrmEye != null)
				{
					if (player.gameObject.GetComponent<VRMEyePositionSync>() == null) player.gameObject.AddComponent<VRMEyePositionSync>().Setup(vrmEye);
					else player.gameObject.GetComponent<VRMEyePositionSync>().Setup(vrmEye);
				}
			}

			// MToonの場合環境光の影響をカラーに反映する
			if (settings.UseMToonShader)
			{
				if (vrmModel.GetComponent<MToonColorSync>() == null) vrmModel.AddComponent<MToonColorSync>().Setup(vrmModel);
				else vrmModel.GetComponent<MToonColorSync>().Setup(vrmModel);
			}

			// SpringBone設定
			var stiffness = settings.SpringBoneStiffness;
			var gravity = settings.SpringBoneGravityPower;
			foreach (var springBone in vrmModel.GetComponentsInChildren<VRMSpringBone>())
			{
				springBone.m_stiffnessForce *= stiffness;
				springBone.m_gravityPower *= gravity;
				springBone.m_updateType = VRMSpringBone.SpringBoneUpdateType.FixedUpdate;
				springBone.m_center = null;
			}
		}

		private static void SharedVRMLoaded(long sender)
		{
			var data = sharedVRMLoadingProcesses[sender];
			
			foreach (var player in Player.GetAllPlayers())
			{
				ref var nview = ref AccessTools.FieldRefAccess<Player, ZNetView>("m_nview").Invoke(player);
				if (nview.GetZDO().m_owner == sender)
				{
					string playerName = player.GetPlayerName();
					if (playerName == "" || playerName == "...") playerName = Game.instance.GetPlayerProfile().GetName();

					if (!data.UseExistingSettings)
					{
						Settings.AddSettingsRaw(playerName, data.Settings.Split('\n'));
					}

					bool vrmSuccess = false;
					if (!data.UseExistingData)
					{
						var settings = Settings.GetSettings(playerName);
						
						var scale = settings.ModelScale;

						byte[] vrmBytes = data.GetVRMData();
						VRM vrm = new VRM(ImportVRM(data.GetVRMData(), scale), playerName);
						vrm = VRMModels.RegisterVrm(vrm, player.GetComponentInChildren<LODGroup>());
						if (vrm != null)
						{
							vrm.IsShared = true;

							vrm.SrcBytes = vrmBytes;
							vrm.RecalculateSrcBytesHash();
							vrm.SrcBytes = null;
							
							vrmSuccess = true;
						}
					}

					if (vrmSuccess)
					{
						SetVrmToPlayer(player, playerName);

						if (!data.UseExistingSettings)
						{
							VRMModels.VrmDic[playerName].RecalculateSettingsHash();
						}
					}
					else
					{
						Debug.LogError("[ValheimVRM] failed to set setup vrm, downloaded from " + playerName);
					}

					break;
				}
			}
			
			sharedVRMLoadingProcesses.Remove(sender);
		}
	}
}