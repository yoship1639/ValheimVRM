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

	public static class VRMModels
	{
		public static Dictionary<string, byte[]> VrmBufDic = new Dictionary<string, byte[]>();
		public static Dictionary<Player, GameObject> PlayerToVrmDic = new Dictionary<Player, GameObject>();
		public static Dictionary<Player, string> PlayerToNameDic = new Dictionary<Player, string>();
	}

	//[HarmonyPatch(typeof(FejdStartup), "InitializeSteam")]
	//static class Patch_FejdStartup_InitializeSteam
	//{
	//	[HarmonyPrefix]
	//	static bool Prefix(FejdStartup __instance, out bool __result)
	//	{
	//		__result = true;
	//		return false;
	//	}
	//}

	[HarmonyPatch(typeof(VisEquipment), "UpdateLodgroup")]
	static class Patch_VisEquipment_UpdateLodgroup
	{
		[HarmonyPostfix]
		static void Postfix(VisEquipment __instance)
		{
			if (!__instance.m_isPlayer) return;
			var player = __instance.GetComponent<Player>();
			if (player == null || !VRMModels.PlayerToVrmDic.ContainsKey(player)) return;

			ref var hair = ref AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_hairItemInstance").Invoke(__instance);
			if (hair != null) SetVisible(hair, false);

			ref var beard = ref AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_beardItemInstance").Invoke(__instance);
			if (beard != null) SetVisible(beard, false);

			ref var chestList = ref AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_chestItemInstances").Invoke(__instance);
			if (chestList != null) foreach (var chest in chestList) SetVisible(chest, false);

			ref var legList = ref AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_legItemInstances").Invoke(__instance);
			if (legList != null) foreach (var leg in legList) SetVisible(leg, false);

			ref var shoulderList = ref AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_shoulderItemInstances").Invoke(__instance);
			if (shoulderList != null) foreach (var shoulder in shoulderList) SetVisible(shoulder, false);

			ref var utilityList = ref AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_utilityItemInstances").Invoke(__instance);
			if (utilityList != null) foreach (var utility in utilityList) SetVisible(utility, false);

			ref var helmet = ref AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_helmetItemInstance").Invoke(__instance);
			if (helmet != null) SetVisible(helmet, false);
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

			if (VRMModels.PlayerToVrmDic.TryGetValue((Player)__instance, out var vrm))
			{
				vrm.transform.SetParent(ragdoll.transform);
				vrm.GetComponent<VRMAnimationSync>().Setup(ragAnim, true);
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
			if (VRMModels.PlayerToNameDic.ContainsKey(__instance)) name = VRMModels.PlayerToNameDic[__instance];
			if (name != null && Settings.ReadBool(name, "FixCameraHeight", true))
			{
				GameObject.Destroy(__instance.GetComponent<EyeSync>());
			}
		}
	}

	[HarmonyPatch(typeof(Player), "Awake")]
	static class Patch_Player_Awake
	{
		private static Dictionary<string, GameObject> vrmDic = new Dictionary<string, GameObject>();

		[HarmonyPostfix]
		static void Postfix(Player __instance)
		{
			var playerName = __instance.GetPlayerName();
			if (playerName == "" || playerName == "...") playerName = Game.instance != null ? Game.instance.GetPlayerProfile().GetName() : null;
			if (!string.IsNullOrEmpty(playerName) && !vrmDic.ContainsKey(playerName))
			{
				var path = Environment.CurrentDirectory + $"/ValheimVRM/{playerName}.vrm";

				ref var m_nview = ref AccessTools.FieldRefAccess<Player, ZNetView>("m_nview").Invoke(__instance);
				byte[] vrmData = null;
				if (m_nview != null && m_nview.GetZDO() != null) vrmData = m_nview.GetZDO().GetByteArray("vrmData");

				if (vrmData == null && !File.Exists(path))
				{
					Debug.LogError("[ValheimVRM] VRMファイルが見つかりません.");
					Debug.LogError("[ValheimVRM] 読み込み予定だったVRMファイルパス: " + path);
				}
				else
				{
					if (!Settings.ContainsSettings(playerName))
					{
						if (vrmData == null && !Settings.AddSettingsFromFile(playerName))
						{
							Debug.LogWarning("[ValheimVRM] 設定ファイルが見つかりません.以下の設定ファイルが存在するか確認してください: " + Settings.PlayerSettingsPath(playerName));
						}
						else if (vrmData != null)
						{
							if (m_nview != null && m_nview.GetZDO() != null)
							{
								var settings = m_nview.GetZDO().GetString("vrmSettings");
								if (settings != "") Settings.AddSettings(playerName, settings.Split('\n'));
								else Debug.LogWarning("[ValheimVRM] 設定ファイルが見つかりませんでした: " + playerName);
							}
						}
					}

					var scale = Settings.ReadFloat(playerName, "ModelScale", 1.1f);
					var orgVrm = vrmData != null ? ImportVRM(vrmData, scale) : ImportVRM(path, scale);
					if (orgVrm != null)
					{
						GameObject.DontDestroyOnLoad(orgVrm);
						vrmDic[playerName] = orgVrm;
						VRMModels.VrmBufDic[playerName] = File.ReadAllBytes(path);

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
						if (Settings.ReadBool(playerName, "UseMToonShader", false))
						{
							foreach (var smr in orgVrm.GetComponentsInChildren<SkinnedMeshRenderer>())
							{
								foreach (var mat in smr.materials)
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
						}
						else
						{
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
						}
						
						orgVrm.SetActive(false);

						// VRMデータの共有設定
						if (Settings.ReadBool(playerName, "AllowVRMShare", false))
						{
							//ref var m_nview = ref AccessTools.FieldRefAccess<Player, ZNetView>("m_nview").Invoke(__instance);
							//if (m_nview.GetZDO() != null && VRMModels.VrmBufDic.ContainsKey(playerName))
							//{
							//	Debug.LogError("VRMデータをセット");
							//	m_nview.GetZDO().Set("vrmData", VRMModels.VrmBufDic[playerName]);
							//	m_nview.GetZDO().Set("vrmSettings", string.Join("\n", Settings.GetSettings(playerName)));
							//}
						}
					}
				}
			}

			if (!string.IsNullOrEmpty(playerName) && vrmDic.ContainsKey(playerName))
			{
				var vrmModel = GameObject.Instantiate(vrmDic[playerName]);
				VRMModels.PlayerToVrmDic[__instance] = vrmModel;
				VRMModels.PlayerToNameDic[__instance] = playerName;
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
				var offsetY = Settings.ReadFloat(playerName, "ModelOffsetY");
				if (vrmModel.GetComponent<VRMAnimationSync>() == null) vrmModel.AddComponent<VRMAnimationSync>().Setup(orgAnim, false, offsetY);
				else vrmModel.GetComponent<VRMAnimationSync>().Setup(orgAnim, false, offsetY);

				// カメラ位置調整
				if (Settings.ReadBool(playerName, "FixCameraHeight", true))
				{
					var vrmEye = vrmModel.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftEye);
					if (__instance.gameObject.GetComponent<EyeSync>() == null) __instance.gameObject.AddComponent<EyeSync>().Setup(vrmEye);
					else __instance.gameObject.GetComponent<EyeSync>().Setup(vrmEye);
				}

				// MToonの場合環境光の影響をカラーに反映する
				if (Settings.ReadBool(playerName, "UseMToonShader", false))
				{
					if (vrmModel.GetComponent<MToonColorSync>() == null) vrmModel.AddComponent<MToonColorSync>().Setup(vrmModel);
					else vrmModel.GetComponent<MToonColorSync>().Setup(vrmModel);
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

					Debug.Log("[ValheimVRM] VRM読み込み成功");

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
		private float offset;

		public void Setup(Animator orgAnim, bool isRagdoll = false, float offset = 0.0f)
		{
			this.ragdoll = isRagdoll;
			this.offset = offset;
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
			pos.y = posY + height + offset;
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

	public class MToonColorSync : MonoBehaviour
	{
		class MatColor
		{
			public Material mat;
			public Color color;
			public Color shadeColor;
			public Color emission;
			public bool hasColor;
			public bool hasShadeColor;
			public bool hasEmission;
		}

		//private int _SunFogColor;
		private int _SunColor;
		private int _AmbientColor;

		private List<MatColor> matColors = new List<MatColor>();

		void Awake()
		{
			//_SunFogColor = Shader.PropertyToID("_SunFogColor");
			_SunColor = Shader.PropertyToID("_SunColor");
			_AmbientColor = Shader.PropertyToID("_AmbientColor");
		}

		public void Setup(GameObject vrm)
		{
			matColors.Clear();
			foreach (var smr in vrm.GetComponentsInChildren<SkinnedMeshRenderer>())
			{
				foreach (var mat in smr.materials)
				{
					if (!matColors.Exists(m => m.mat == mat))
					{
						matColors.Add(new MatColor()
						{
							mat = mat,
							color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white,
							shadeColor = mat.HasProperty("_ShadeColor") ? mat.GetColor("_ShadeColor") : Color.white,
							emission = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black,
							hasColor = mat.HasProperty("_Color"),
							hasShadeColor = mat.HasProperty("_ShadeColor"),
							hasEmission = mat.HasProperty("_EmissionColor"),
						});
					}
				}
			}
		}

		void Update()
		{
			//var fog = Shader.GetGlobalColor(_SunFogColor);
			var sun = Shader.GetGlobalColor(_SunColor);
			var amb = Shader.GetGlobalColor(_AmbientColor);
			var sunAmb = sun + amb;
			if (sunAmb.maxColorComponent > 0.7f) sunAmb /= 0.3f + sunAmb.maxColorComponent;

			foreach (var matColor in matColors)
			{
				var col = matColor.color * sunAmb;
				col.a = matColor.color.a;
				if (col.maxColorComponent > 1.0f) col /= col.maxColorComponent;

				var shadeCol = matColor.shadeColor * sunAmb;
				shadeCol.a = matColor.shadeColor.a;
				if (shadeCol.maxColorComponent > 1.0f) shadeCol /= shadeCol.maxColorComponent;

				var emi = matColor.emission * sunAmb.grayscale;

				if (matColor.hasColor) matColor.mat.SetColor("_Color", col);
				if (matColor.hasShadeColor) matColor.mat.SetColor("_ShadeColor", shadeCol);
				if (matColor.hasEmission) matColor.mat.SetColor("_EmissionColor", emi);
			}
		}
	}
}