using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ValheimVRM
{
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
}
