using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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

	public static class VrmManager
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
			if (player == null || !VrmManager.PlayerToVrmInstance.ContainsKey(player)) return;
			
			var name = VrmManager.PlayerToName[player];

			var settings = Settings.GetSettings(name);

			var hair = __instance.GetField<VisEquipment, GameObject>("m_hairItemInstance");
			if (hair != null) SetVisible(hair, false);

			var beard = __instance.GetField<VisEquipment, GameObject>("m_beardItemInstance");
			if (beard != null) SetVisible(beard, false);

			var chestList = __instance.GetField<VisEquipment, List<GameObject>>("m_chestItemInstances");
			if (chestList != null)
			{
				if (!settings.ChestVisible)
				{
					foreach (var chest in chestList) SetVisible(chest, false);
				}
			}

			var legList = __instance.GetField<VisEquipment, List<GameObject>>("m_legItemInstances");
			if (legList != null)
			{
				if (!settings.LegsVisible)
				{
					foreach (var leg in legList) SetVisible(leg, false);
				}
			}

			var shoulderList = __instance.GetField<VisEquipment, List<GameObject>>("m_shoulderItemInstances");
			if (shoulderList != null)
			{
				if (shoulderList != null)
				{
					if (!settings.ShouldersVisible)
					{
						foreach (var shoulder in shoulderList) SetVisible(shoulder, false);
					}
				}
			}

			var utilityList = __instance.GetField<VisEquipment, List<GameObject>>("m_utilityItemInstances");
			if (utilityList != null)
			{
				if (!settings.UtilityVisible)
				{
					foreach (var utility in utilityList) SetVisible(utility, false);
				}
			}

			var helmet = __instance.GetField<VisEquipment, GameObject>("m_helmetItemInstance");
			if (helmet != null)
			{
				if (!settings.HelmetVisible)
				{
					SetVisible(helmet, false);
				}
				else
				{
					helmet.transform.localScale = settings.HelmetScale;
					helmet.transform.localPosition = settings.HelmetOffset;
				}
			}

			// 武器位置合わせ
			float equipmentScale = settings.EquipmentScale;
			Vector3 equipmentScaleVector = new Vector3(equipmentScale, equipmentScale, equipmentScale);
			
			var leftItem = __instance.GetField<VisEquipment, GameObject>("m_leftItemInstance");
			if (leftItem != null)
			{
				leftItem.transform.localPosition = settings.LeftHandItemPos;
				leftItem.transform.localScale = equipmentScaleVector;
			}

			var rightItem = __instance.GetField<VisEquipment, GameObject>("m_rightItemInstance");
			if (rightItem != null)
			{
				rightItem.transform.localPosition = settings.RightHandItemPos;
				rightItem.transform.localScale = equipmentScaleVector;
			}
			
			// divided  by 100 to keep the settings file positions in the same number range. (position offset appears to be on the world, not local)
			var rightBackItem = __instance.GetField<VisEquipment, GameObject>("m_rightBackItemInstance");
			if (rightBackItem != null)
			{
				Vector3 offset = rightBackItem.transform.parent == __instance.m_backTool ? settings.RightHandBackItemToolPos : settings.RightHandBackItemPos;
				
				rightBackItem.transform.localPosition = offset / 100.0f;
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

				if (VrmManager.PlayerToVrmInstance.TryGetValue(player, out var vrm))
				{
					vrm.transform.SetParent(ragdoll.transform);
					vrm.GetComponent<VRMAnimationSync>().Setup(ragAnim, Settings.GetSettings(VrmManager.PlayerToName[player]), true);
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

			if (VrmManager.PlayerToVrmInstance.TryGetValue((Player)__instance, out var vrm))
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
			if (VrmManager.PlayerToName.ContainsKey(__instance)) name = VrmManager.PlayerToName[__instance];
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

			if (VrmManager.PlayerToVrmInstance.TryGetValue(player, out var vrm))
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

	[HarmonyPatch(typeof(Player), "GetStealthFactor")]
	static class Patch_Player_GetStealthFactor
	{
		[HarmonyPostfix]
		static void Postfix(Player __instance, ref float __result)
		{
			string playerName;
			if (VrmManager.PlayerToName.TryGetValue(__instance, out playerName))
			{
				var settings = Settings.GetSettings(playerName);
				if (settings != null)
				{
					__result = Mathf.Clamp01(__result /= settings.StealthScale);
				}
			}
		}
	}

	// Remove stealth factor check, show stealth hud only if crouching
	[HarmonyPatch(typeof(Hud), "UpdateStealth")]
	public static class Patch_Hud_UpdateStealth
	{
		[HarmonyReversePatch()]
		static void Postfix(Hud __instance, Player player, float bowDrawPercentage)
		{
			if (player.IsCrouching() && (double) bowDrawPercentage == 0.0)
			{
				if (player.IsSensed())
				{
					__instance.m_targetedAlert.SetActive(true);
					__instance.m_targeted.SetActive(false);
					__instance.m_hidden.SetActive(false);
				}
				else if (player.IsTargeted())
				{
					__instance.m_targetedAlert.SetActive(false);
					__instance.m_targeted.SetActive(true);
					__instance.m_hidden.SetActive(false);
				}
				else
				{
					__instance.m_targetedAlert.SetActive(false);
					__instance.m_targeted.SetActive(false);
					__instance.m_hidden.SetActive(true);
				}
				__instance.m_stealthBar.gameObject.SetActive(true);
				__instance.m_stealthBar.SetValue(player.GetStealthFactor());
			}
			else
			{
				__instance.m_targetedAlert.SetActive(false);
				__instance.m_hidden.SetActive(false);
				__instance.m_targeted.SetActive(false);
				__instance.m_stealthBar.gameObject.SetActive(false);
			}
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

				if (attack == null) return;
				
				ref float time = ref AccessTools.FieldRefAccess<Attack, float>("m_time").Invoke(attack);
				if (time != 0) return;
				
				string playerName;
				if (VrmManager.PlayerToName.TryGetValue(player, out playerName))
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
		[HarmonyReversePatch]
		static void Postfix(Player __instance, out float hp, out float stamina)
		{
			float baseHealthScale = 1f;
			float foodHealthScale = 1f;
			float baseStaminaScale = 1f;
			float foodStaminaScale = 1f;
			
			string playerName;
			if (VrmManager.PlayerToName.TryGetValue(__instance, out playerName))
			{
				var settings = Settings.GetSettings(playerName);
				if (settings != null)
				{
					baseHealthScale = settings.BaseHealthScale;
					foodHealthScale = settings.FoodHealthScale;
					baseStaminaScale = settings.BaseStaminaScale;
					foodStaminaScale = settings.FoodStaminaScale;
				}
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

	[HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
	static class Patch_Player_UpdatePlacementGhost
	{
		[HarmonyPostfix]
		static void UpdatePlacementGhost(Player __instance, bool flashGuardStone)
		{
			var fi = typeof(Player).GetField("m_placementStatus", BindingFlags.NonPublic | BindingFlags.Instance);
			var val = (int)fi.GetValue(__instance);
			if (val == 10)
			{
				fi.SetValue(__instance, 0);
			}
		}
	}

	[HarmonyPatch(typeof(Game), "SpawnPlayer")]
	static class Patch_Game_SpawnPlayer
	{
		[HarmonyPostfix]
		static void Postfix(Game __instance, bool ___m_firstSpawn, Player __result)
		{
			if (___m_firstSpawn)
			{
				__result.GetComponent<VrmController>().ShareVrm();
				__result.GetComponent<VrmController>().QueryAllVrm();
			}
		}
	}
	
	[HarmonyPatch(typeof(Player), "OnDestroy")]
	static class Patch_Player_OnDestroy
	{
		[HarmonyPostfix]
		static void Postfix(Player __instance)
		{
			VrmManager.PlayerToName.Remove(__instance);
            VrmManager.PlayerToVrmInstance.Remove(__instance);
		}
	}
	
	[HarmonyPatch(typeof(ItemDrop.ItemData), "GetTooltip", new Type[] {typeof(ItemDrop.ItemData), typeof(int), typeof(bool)})]
	static class Patch_ItemData_GetTooltip
	{
		public static float GetFoodHealth(float baseValue, Player player)
		{
			string name;
			if (VrmManager.PlayerToName.TryGetValue(player, out name))
			{
				var settings = Settings.GetSettings(name);
				if (settings != null)
				{
					return baseValue * settings.FoodHealthScale;
				}
			}

			return baseValue;
		}
		
		public static float GetFoodStamina(float baseValue, Player player)
		{
			string name;
			if (VrmManager.PlayerToName.TryGetValue(player, out name))
			{
				var settings = Settings.GetSettings(name);
				if (settings != null)
				{
					return baseValue * settings.FoodStaminaScale;
				}
			}

			return baseValue;
		}
		
		public static float GetFoodDuration(float baseValue, Player player)
		{
			string name;
			if (VrmManager.PlayerToName.TryGetValue(player, out name))
			{
				var settings = Settings.GetSettings(name);
				if (settings != null)
				{
					return baseValue * settings.DigestionTimeScale;
				}
			}

			return baseValue;
		}
		
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			List<CodeInstruction> instructionList = new List<CodeInstruction>(instructions);

			{
				int i = instructionList.FindOp(OpCodes.Ldstr, "\n$item_food_health: <color=#ff8080ff>{0}</color>  ($item_current:<color=yellow>{1}</color>)");
				if (i >= 0)
				{
					int j = instructionList.FindOp(OpCodes.Ldfld, Utils.GetField<ItemDrop.ItemData.SharedData>("m_food"), i);
					if (j >= 0)
					{
						instructionList.InsertRange(j + 1, new CodeInstruction[]
						{
							new CodeInstruction(OpCodes.Ldloc_0),
							new CodeInstruction(OpCodes.Call, typeof(Patch_ItemData_GetTooltip).GetMethod("GetFoodHealth"))
						});
					}
				}
			}

			{
				int i = instructionList.FindOp(OpCodes.Ldstr, "\n$item_food_stamina: <color=#ffff80ff>{0}</color>  ($item_current:<color=yellow>{1}</color>)");
				if (i >= 0)
				{
					int j = instructionList.FindOp(OpCodes.Ldfld, Utils.GetField<ItemDrop.ItemData.SharedData>("m_foodStamina"), i);
					if (j >= 0)
					{
						instructionList.InsertRange(j + 1, new CodeInstruction[]
						{
							new CodeInstruction(OpCodes.Ldloc_0),
							new CodeInstruction(OpCodes.Call, typeof(Patch_ItemData_GetTooltip).GetMethod("GetFoodStamina"))
						});
					}
				}
			}

			{
				int i = instructionList.FindOp(OpCodes.Ldstr, "\n$item_food_duration: <color=orange>{0}</color>");
				if (i >= 0)
				{
					int j = instructionList.FindOp(OpCodes.Ldfld, Utils.GetField<ItemDrop.ItemData.SharedData>("m_foodBurnTime"), i);
					if (j >= 0)
					{
						instructionList.InsertRange(j + 1, new CodeInstruction[]
						{
							new CodeInstruction(OpCodes.Ldloc_0),
							new CodeInstruction(OpCodes.Call, typeof(Patch_ItemData_GetTooltip).GetMethod("GetFoodDuration"))
						});
					}
				}
			}

			return instructionList;
		}
	}

	[HarmonyPatch(typeof(Player), "EatFood")]
	static class Patch_Player_EatFood
	{
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var instructionList = new List<CodeInstruction>(instructions);

			for (int i = instructionList.FindOp(OpCodes.Ldstr, " $item_food_health ") - 1; i >= 0; i--)
			{
				if (instructionList[i].IsOp(OpCodes.Ldfld, Utils.GetField<ItemDrop.ItemData.SharedData>("m_food")))
				{
					instructionList.InsertRange(i + 1, new CodeInstruction[]
					{
						new CodeInstruction(OpCodes.Ldarg_0),
						new CodeInstruction(OpCodes.Call, typeof(Patch_ItemData_GetTooltip).GetMethod("GetFoodHealth"))
					});
					
					break;
				}
			}
			
			for (int i = instructionList.FindOp(OpCodes.Ldstr, " $item_food_stamina ") - 1; i >= 0; i--)
			{
				if (instructionList[i].IsOp(OpCodes.Ldfld, Utils.GetField<ItemDrop.ItemData.SharedData>("m_foodStamina")))
				{
					instructionList.InsertRange(i + 1, new CodeInstruction[]
					{
						new CodeInstruction(OpCodes.Ldarg_0),
						new CodeInstruction(OpCodes.Call, typeof(Patch_ItemData_GetTooltip).GetMethod("GetFoodStamina"))
					});
					
					break;
				}
			}
			
			return instructionList;
		}
	}
	
	[HarmonyPatch(typeof(Player), "UpdateFood")]
	static class Patch_Player_UpdateFood
	{
		public static float GetDigestionSpeed(float baseValue, Player player)
		{
			string name;
			if (VrmManager.PlayerToName.TryGetValue(player, out name))
			{
				var settings = Settings.GetSettings(name);
				if (settings != null)
				{
					return baseValue / settings.DigestionTimeScale;
				}
			}

			return baseValue;
		}
		
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var instructionList = new List<CodeInstruction>(instructions);

			for (int i = instructionList.FindOp(OpCodes.Ldfld, Utils.GetField<Player.Food>("m_time")) + 1; i < instructionList.Count; i++)
			{
				if (instructionList[i].opcode == OpCodes.Ldc_R4 && (float)instructionList[i].operand == 1.0f)
				{
					instructionList.InsertRange(i + 1, new CodeInstruction[]
					{
						new CodeInstruction(OpCodes.Ldarg_0),
						new CodeInstruction(OpCodes.Call, typeof(Patch_Player_UpdateFood).GetMethod("GetDigestionSpeed"))
					});
					
					break;
				}
			}

			return instructionList;
		}
	}

	[HarmonyPatch(typeof(Player), "Awake")]
	static class Patch_Player_Awake
	{
		[HarmonyPostfix]
		static void Postfix(Player __instance, ZNetView ___m_nview)
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
			
			VrmManager.PlayerToName[__instance] = playerName;
			
			bool isInMenu = __instance.gameObject.scene.name == "start";

			if (isInMenu)
			{
				var names = new List<string>(VrmManager.VrmDic.Keys);
				foreach (var name in names)
				{
					var vrm = VrmManager.VrmDic[name];
					if (vrm.Source == VRM.SourceType.Shared)
					{
						VrmManager.VrmDic.Remove(name);
						Settings.RemoveSettings(name);
					}
				}

                VrmController.CleanupLoadings();
			}

			bool online = ___m_nview.GetZDO() != null;

			var vrmController = __instance.gameObject.AddComponent<VrmController>();

			if (!string.IsNullOrEmpty(playerName))
			{
				bool settingsUpdated = false;
				
				var path = Path.Combine(Environment.CurrentDirectory, "ValheimVRM", $"{playerName}.vrm");
				var sharedPath = Path.Combine(Environment.CurrentDirectory, "ValheimVRM", "Shared", $"{playerName}.vrm");

				if (!Settings.ContainsSettings(playerName) || Settings.globalSettings.ReloadInMenu && isInMenu)
				{
					if (File.Exists(path))
					{
						Settings.AddSettingsFromFile(playerName, false);
						settingsUpdated = true;
					}
					else if (File.Exists(sharedPath))
					{
						Settings.AddSettingsFromFile(playerName, true);
						settingsUpdated = true;
					}
				}

				VRM vrm = null;
				
				var settings = Settings.GetSettings(playerName);

				if (settings != null)
				{
					if (!VrmManager.VrmDic.ContainsKey(playerName) || Settings.globalSettings.ReloadInMenu && isInMenu)
					{
						if (File.Exists(path))
						{
							var vrmVisual = VRM.ImportVisual(path, settings.ModelScale);
							if (vrmVisual != null)
							{
								vrm = new VRM(vrmVisual, playerName);
								vrm = VrmManager.RegisterVrm(vrm, __instance.GetComponentInChildren<LODGroup>());
								if (vrm != null)
								{
									vrm.Src = File.ReadAllBytes(path);
									vrm.RecalculateSrcBytesHash();
								}
							}
						}
						else if (File.Exists(sharedPath))
						{
							var vrmVisual = VRM.ImportVisual(sharedPath, settings.ModelScale);
							if (vrmVisual != null)
							{
								vrm = new VRM(vrmVisual, playerName);
								vrm = VrmManager.RegisterVrm(vrm, __instance.GetComponentInChildren<LODGroup>());
								if (vrm != null)
								{
									vrm.Src = File.ReadAllBytes(sharedPath);
									vrm.RecalculateSrcBytesHash();

									vrm.Source = VRM.SourceType.Shared;
								}
							}
						}
						else
						{ //default character stuff
							if (!VrmManager.VrmDic.ContainsKey("___Default"))
							{
								var defaultPath = Path.Combine(Environment.CurrentDirectory, "ValheimVRM", "___Default.vrm");

								if (File.Exists(defaultPath))
								{
									var vrmVisual = VRM.ImportVisual(defaultPath, settings.ModelScale);
									if (vrmVisual != null)
									{
										vrm = new VRM(vrmVisual, "___Default");
										vrm = VrmManager.RegisterVrm(vrm, __instance.GetComponentInChildren<LODGroup>());
										if (vrm != null)
										{
											vrm.Src = File.ReadAllBytes(defaultPath);
											vrm.RecalculateSrcBytesHash();
										}
									}
								}
		
							}
							else
							{
								vrm = VrmManager.VrmDic["___Default"];
							}
		
						}

					}
					else
					{
						vrm = VrmManager.VrmDic[playerName];
					}
				}
				else
				{
					Debug.LogError("Settings are still null");

				}

				if (vrm != null)
				{
                    if (settingsUpdated)
					{
						vrm.RecalculateSettingsHash();
					}
						
					vrm.SetToPlayer(__instance);
				}
			}
		}
	}
}