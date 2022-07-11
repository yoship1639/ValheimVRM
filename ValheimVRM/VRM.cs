using System;
using System.IO;
using HarmonyLib;
using UniGLTF;
using UnityEngine;
using VRM;
using Object = UnityEngine.Object;

namespace ValheimVRM
{
    public class VRM
	{
		public enum SourceType
		{
			Local,  // my VRM from my computer
			Shared // VRM, downloaded from other player
		}
		
		public GameObject VisualModel { get; private set; }
		public byte[] Src;
		public byte[] SrcHash;
		public byte[] SettingsHash;
		public string Name { get; private set; }
		public SourceType Source = SourceType.Local;

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
				SrcHash = md5.ComputeHash(Src);
			}
		}

		public void RecalculateSettingsHash()
		{
			using (var md5 = System.Security.Cryptography.MD5.Create())
			{
				byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(Settings.GetSettings(Name).ToStringDiffOnly());
				SettingsHash = md5.ComputeHash(inputBytes);
			}
		}
		
		public static GameObject ImportVisual(string path, float scale)
		{
			Debug.Log("[ValheimVRM] loading vrm from file, " + new FileInfo(path).Length + " bytes");
			Debug.Log("[ValheimVRM] vrm file path: " + path);
			
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

					context.Root.transform.localScale = Vector3.one * scale;

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

		public static GameObject ImportVisual(byte[] buf, float scale)
		{
			Debug.Log("[ValheimVRM] loading vrm from memory, " + buf.Length + " bytes");
			
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

					context.Root.transform.localScale = Vector3.one * scale;

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
		
		public void SetToPlayer(Player player)
		{
			var settings = Settings.GetSettings(Name);
			
			player.m_maxInteractDistance *= settings.InteractionDistanceScale;
			player.m_maxPlaceDistance *= settings.InteractionDistanceScale;
			player.m_swimDepth *= settings.SwimDepthScale;
			player.m_swimSpeed *= settings.SwimSpeedScale;
			player.m_maxCarryWeight *= settings.WeightLimitScale;
			player.m_walkSpeed *= settings.MovementSpeedScale;
			player.m_runSpeed *= settings.MovementSpeedScale;
			player.m_jumpForce *= settings.JumpForceScale;
			
			var vrmModel = Object.Instantiate(VisualModel);
			VrmManager.PlayerToVrmInstance[player] = vrmModel;
			vrmModel.name = "VRM_Visual";
			vrmModel.SetActive(true);
			player.GetComponent<VrmController>().visual = vrmModel;

			var oldModel = player.GetComponentInChildren<Animator>().transform.parent.Find("VRM_Visual");
			if (oldModel != null)
			{
				Object.Destroy(oldModel);
			}
			
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
			foreach (var springBone in vrmModel.GetComponentsInChildren<VRMSpringBone>())
			{
				springBone.m_stiffnessForce *= settings.SpringBoneStiffness;
				springBone.m_gravityPower *= settings.SpringBoneGravityPower;
				springBone.m_updateType = VRMSpringBone.SpringBoneUpdateType.FixedUpdate;
				springBone.m_center = null;
			}
			
			player.GetComponent<VrmController>().ReloadSpringBones();
        }
    }
}