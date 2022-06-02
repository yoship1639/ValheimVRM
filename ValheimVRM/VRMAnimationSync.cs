using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ValheimVRM
{
	[DefaultExecutionOrder(int.MaxValue)]
	public class VRMAnimationSync : MonoBehaviour
	{
		private Animator orgAnim, vrmAnim;
		private HumanPoseHandler orgPose, vrmPose;
		private HumanPose hp = new HumanPose();
		private bool ragdoll;
		private Settings.VrmSettingsContainer settings;
		private Vector3? adjustPos;
		private int stateHash;

		public void Setup(Animator orgAnim, Settings.VrmSettingsContainer settings, bool isRagdoll = false)
		{
			this.ragdoll = isRagdoll;
			this.settings = settings;
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
		}

		void OnDestroy()
		{
			if (orgPose != null)
				orgPose.Dispose();
			if (vrmPose != null)
				vrmPose.Dispose();
		}

		private static int prevHash = 0;

		const int FirstTime         = -161139084;
		const int Usually           =  229373857;  // standing idle
		const int FirstRise         = -1536343465; // stand up upon login
		const int RiseUp            = -805461806;
		const int StartToSitDown    =  890925016;
		const int SittingIdle       = -1544306596;
		const int StandingUpFromSit = -805461806;
		const int SittingChair      = -1829310159;
		const int SittingThrone     =  1271596;
		const int SittingShip       = -675369009;
		const int StartSleeping     =  337039637;
		const int Sleeping          = -1603096;
		const int GetUpFromBed      = -496559199;
		const int Crouch            = -2015693266;
		const int HoldingMast       = -2110678410;
		const int HoldingDragon     = -2076823180; // that thing in a front of longship

		private static List<int> adjustHipHashes = new List<int>()
		{
			SittingChair,
			SittingThrone,
			SittingShip,
			Sleeping
		};

		void Update()
		{
			vrmAnim.transform.localPosition = Vector3.zero;
			if (!ragdoll)
			{
				for (var i = 0; i < 55; i++)
				{
					var orgTrans = orgAnim.GetBoneTransform((HumanBodyBones)i);
					var vrmTrans = vrmAnim.GetBoneTransform((HumanBodyBones)i);

					if (i > 0 && orgTrans != null && vrmTrans != null)
					{
						if ((HumanBodyBones)i == HumanBodyBones.LeftFoot || (HumanBodyBones)i == HumanBodyBones.RightFoot) {
							orgTrans.position = vrmTrans.position;
						} else {
							orgTrans.position = vrmTrans.position + Vector3.up * settings.ModelOffsetY;
						}
					}
				}
			}

			vrmAnim.transform.localPosition += Vector3.up * settings.ModelOffsetY;
		}
		
		void LateUpdate()
		{
			float playerScaleFactor = settings.PlayerHeight / 1.85f;
			
			vrmAnim.transform.localPosition = Vector3.zero;
			
			orgPose.GetHumanPose(ref hp);
			vrmPose.SetHumanPose(ref hp);

			var newStateHash = orgAnim.GetCurrentAnimatorStateInfo(0).shortNameHash;
			var adjustFromHips = adjustHipHashes.Contains(newStateHash);

			//if (newStateHash != prevHash)
			//{
			//	prevHash = newStateHash;
			//	Debug.Log(orgAnim.GetCurrentAnimatorClipInfo(0)[0].clip.name + ": " + newStateHash);
			//}

			var vrmHip = vrmAnim.GetBoneTransform(HumanBodyBones.Hips);
			var orgHip = orgAnim.GetBoneTransform(HumanBodyBones.Hips);
			
			vrmHip.position = orgAnim.GetBoneTransform(HumanBodyBones.Hips).position;

			var newAdjustPos = new Vector3();

			if (!adjustFromHips)
			{
				// Foot doesn't seem to work as intended on characters with non-standard sizes
				// It's safer to use provided player height to evaluate the offset

				Vector3 curOrgHipPos = orgHip.position - orgHip.parent.position;
				Vector3 curVrmHipPos = curOrgHipPos * playerScaleFactor;
				
				newAdjustPos = curVrmHipPos - curOrgHipPos;
			}

			float interpSpeed = Time.deltaTime * 5;
			
			switch (newStateHash)
			{
				case StartToSitDown:
				case SittingIdle:
					newAdjustPos += orgHip.transform.rotation * settings.SittingIdleOffset;
					break;
				
				case SittingChair:
					newAdjustPos += orgHip.transform.rotation * settings.SittingOnChairOffset;
					break;
				
				case SittingThrone:
					newAdjustPos += orgHip.transform.rotation * settings.SittingOnThroneOffset;
					break;
				
				case SittingShip:
					newAdjustPos += orgHip.transform.rotation * settings.SittingOnShipOffset;
					break;
				
				case HoldingMast:
					newAdjustPos += orgHip.transform.rotation * settings.HoldingMastOffset;
					break;
				
				case HoldingDragon:
					newAdjustPos += orgHip.transform.rotation * settings.HoldingDragonOffset;
					break;
				
				case Sleeping:
					newAdjustPos += orgHip.transform.rotation * settings.SleepingOffset;
					break;
				
				default:
					interpSpeed = 1;
					break;
			}

			adjustPos = adjustPos.HasValue ? Vector3.Lerp(adjustPos.Value, newAdjustPos, interpSpeed) : newAdjustPos;
			
			vrmHip.position += adjustPos.Value;
			
			if (!ragdoll)
			{
				for (var i = 0; i < 55; i++)
				{
					var orgTrans = orgAnim.GetBoneTransform((HumanBodyBones)i);
					var vrmTrans = vrmAnim.GetBoneTransform((HumanBodyBones)i);
            
					if (i > 0 && orgTrans != null && vrmTrans != null)
					{
						if ((HumanBodyBones)i == HumanBodyBones.LeftFoot || (HumanBodyBones)i == HumanBodyBones.RightFoot) {
							orgTrans.position = vrmTrans.position;
						} else {
							orgTrans.position = vrmTrans.position + Vector3.up * settings.ModelOffsetY;
						}
					}
				}
			}

			vrmAnim.transform.localPosition += Vector3.up * settings.ModelOffsetY;

			stateHash = newStateHash;
		}
	}
}
