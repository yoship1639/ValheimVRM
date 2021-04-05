using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UniGLTF;
using UnityEngine;
using VRM;

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

	[HarmonyPatch(typeof(Humanoid), "SetupVisEquipment")]
	static class Patch_Humanoid_SetupVisEquipment
	{
		[HarmonyPostfix]
		static void Postfix(Humanoid __instance, VisEquipment visEq, bool isRagdoll)
		{
			if (!__instance.IsPlayer()) return;

			visEq.SetHairItem("");
			visEq.SetBeardItem("");
			visEq.SetChestItem("");
			visEq.SetLegItem("");
			visEq.SetShoulderItem("", 0);
			visEq.SetUtilityItem("");
		}
	}

	[HarmonyPatch(typeof(VisEquipment), "UpdateEquipmentVisuals")]
	static class Patch_VisEquipment_UpdateEquipmentVisuals
	{
		[HarmonyPostfix]
		static void Postfix(VisEquipment __instance)
		{
			if (!__instance.m_isPlayer) return;
			//var player = __instance.GetComponent<Player>();

			// 頭装備は非表示にするだけにする
			ref var helmet = ref AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_helmetItemInstance").Invoke(__instance);
			if (helmet != null) SetVisible(helmet);
		}

		private static void SetVisible(GameObject obj)
		{
			foreach (var mr in obj.GetComponentsInChildren<MeshRenderer>()) mr.enabled = false;
			foreach (var smr in obj.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.enabled = false;
		}
	}

	[HarmonyPatch(typeof(Humanoid), "OnRagdollCreated")]
	static class Patch_Humanoid_OnRagdollCreated
	{
		[HarmonyPostfix]
		static void Postfix(Humanoid __instance, Ragdoll ragdoll)
		{
			if (!__instance.IsPlayer()) return;

			foreach (var smr in ragdoll.GetComponentsInChildren<SkinnedMeshRenderer>())
			{
				smr.forceRenderingOff = true;
				smr.updateWhenOffscreen = true;
			}
			

			var ragAnim = ragdoll.gameObject.AddComponent<Animator>();
			ragAnim.keepAnimatorControllerStateOnDisable = true;
			ragAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

			var orgAnim = AccessTools.FieldRefAccess<Player, Animator>((Player)__instance, "m_animator");
			ragAnim.avatar = orgAnim.avatar;

			if (Patch_Player_Awake.PlayerToVrmDic.TryGetValue((Player)__instance, out var vrm))
			{
				vrm.transform.SetParent(ragdoll.transform);
				vrm.GetComponent<VRMAnimationSync>().Setup(ragAnim, true);
			}
		}
	}

	[HarmonyPatch(typeof(Player), "Awake")]
	static class Patch_Player_Awake
	{
		private static Dictionary<string, GameObject> vrmDic = new Dictionary<string, GameObject>();
		public static Dictionary<Player, GameObject> PlayerToVrmDic = new Dictionary<Player, GameObject>();

		[HarmonyPostfix]
		static void Postfix(Player __instance)
		{
			var playerName = __instance.GetPlayerName();
			if (playerName == "" || playerName == "...") playerName = Game.instance != null ? Game.instance.GetPlayerProfile().GetName() : null;
			if (!string.IsNullOrEmpty(playerName) && !vrmDic.ContainsKey(playerName))
			{
				var path = Environment.CurrentDirectory + $"/ValheimVRM/{playerName}.vrm";

				if (!File.Exists(path))
				{
					Debug.LogError("[ValheimVRM] VRMファイルが見つかりません.");
					Debug.LogError("[ValheimVRM] 読み込み予定だったVRMファイルパス: " + path);
				}
				else
				{
					if (!File.Exists(Settings.PlayerSettingsPath(playerName)))
					{
						Debug.LogWarning("[ValheimVRM] 設定ファイルが見つかりません.以下の設定ファイルが存在するか確認してください: " + Settings.PlayerSettingsPath(playerName));
					}

					var scale = Settings.ReadFloat(playerName, "ModelScale", 1.1f);
					var orgVrm = ImportVRM(path, scale);
					if (orgVrm != null)
					{
						GameObject.DontDestroyOnLoad(orgVrm);
						vrmDic[playerName] = orgVrm;

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

						// シェーダ差し替え
						var brightness = Settings.ReadFloat(playerName, "ModelBrightness", 0.8f);
						var shader = Shader.Find("Custom/Player");
						foreach (var smr in orgVrm.GetComponentsInChildren<SkinnedMeshRenderer>())
						{
							foreach (var mat in smr.materials)
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
						orgVrm.SetActive(false);
					}
				}
			}

			if (!string.IsNullOrEmpty(playerName) && vrmDic.ContainsKey(playerName))
			{
				var vrmModel = GameObject.Instantiate(vrmDic[playerName]);
				PlayerToVrmDic[__instance] = vrmModel;
				vrmModel.SetActive(true);
				vrmModel.transform.SetParent(__instance.GetComponentInChildren<Animator>().transform.parent, false);

				foreach (var smr in __instance.GetVisual().GetComponentsInChildren<SkinnedMeshRenderer>())
				{
					smr.forceRenderingOff = true;
					smr.updateWhenOffscreen = true;
				}

				var orgAnim = AccessTools.FieldRefAccess<Player, Animator>(__instance, "m_animator");
				orgAnim.keepAnimatorControllerStateOnDisable = true;
				orgAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

				vrmModel.transform.localPosition = orgAnim.transform.localPosition;

				// アニメーション同期
				if (vrmModel.GetComponent<VRMAnimationSync>() == null) vrmModel.AddComponent<VRMAnimationSync>().Setup(orgAnim);
				else vrmModel.GetComponent<VRMAnimationSync>().Setup(orgAnim);

				// カメラ位置調整
				if (Settings.ReadBool(playerName, "FixCameraHeight", true))
				{
					var vrmEye = vrmModel.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftEye);
					__instance.gameObject.AddComponent<EyeSync>().Setup(vrmEye);
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

					Debug.Log("[ValheimVRM] VRM読み込み成功");
					Debug.Log("[ValheimVRM] VRMファイルパス: " + path);

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
	}

	[DefaultExecutionOrder(int.MaxValue)]
	public class VRMAnimationSync : MonoBehaviour
	{
		private Animator orgAnim, vrmAnim;
		private HumanPoseHandler orgPose, vrmPose;
		private HumanPose hp = new HumanPose();
		private float height = 0.0f;
		private bool ragdoll;

		public void Setup(Animator orgAnim, bool isRagdoll = false)
		{
			this.ragdoll = isRagdoll;
			this.orgAnim = orgAnim;
			this.vrmAnim = GetComponent<Animator>();
			this.vrmAnim.applyRootMotion = true;
			this.vrmAnim.updateMode = orgAnim.updateMode;
			this.vrmAnim.feetPivotActive = orgAnim.feetPivotActive;
			this.vrmAnim.layersAffectMassCenter = orgAnim.layersAffectMassCenter;
			this.vrmAnim.stabilizeFeet = orgAnim.stabilizeFeet;

			PoseHandlerCreate(orgAnim, vrmAnim);
		}

		void PoseHandlerCreate(Animator org, Animator vrm)
		{
			OnDestroy();
			orgPose = new HumanPoseHandler(org.avatar, org.transform);
			vrmPose = new HumanPoseHandler(vrm.avatar, vrm.transform);

			height = vrmAnim.GetBoneTransform(HumanBodyBones.Hips).position.y - orgAnim.GetBoneTransform(HumanBodyBones.Hips).position.y;
		}

		void OnDestroy()
		{
			if (orgPose != null)
				orgPose.Dispose();
			if (vrmPose != null)
				vrmPose.Dispose();
		}

		void Update()
		{
			if (ragdoll) return;
			for (var i = 0; i < 55; i++)
			{
				var orgTrans = orgAnim.GetBoneTransform((HumanBodyBones)i);
				var vrmTrans = vrmAnim.GetBoneTransform((HumanBodyBones)i);

				if (i > 0 && orgTrans != null && vrmTrans != null)
				{
					orgTrans.position = vrmTrans.position;
				}
			}
		}

		void LateUpdate()
		{
			orgPose.GetHumanPose(ref hp);
			vrmPose.SetHumanPose(ref hp);

			var posY = orgAnim.GetBoneTransform(HumanBodyBones.Hips).position.y;

			if (!ragdoll)
			{
				for (var i = 0; i < 55; i++)
				{
					var orgTrans = orgAnim.GetBoneTransform((HumanBodyBones)i);
					var vrmTrans = vrmAnim.GetBoneTransform((HumanBodyBones)i);

					if (i > 0 && orgTrans != null && vrmTrans != null)
					{
						orgTrans.position = vrmTrans.position;
					}
				}
			}

			var pos = vrmAnim.transform.position;
			pos.y = posY + height;
			vrmAnim.transform.position = pos;
		}
	}

	public class EyeSync : MonoBehaviour
	{
		private Transform vrmEye;
		private Transform orgEye;

		public void Setup(Transform vrmEye)
		{
			this.vrmEye = vrmEye;
			this.orgEye = GetComponent<Player>().m_eye;
		}

		void Update()
		{
			var pos = this.orgEye.position;
			pos.y = this.vrmEye.position.y;
			this.orgEye.position = pos;
		}
	}
}