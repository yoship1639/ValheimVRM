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
		private int oldStateHash;

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

		//private static int prevHash = 0;

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

		private Vector3 StateHashToOffset(int stateHash, out float interpSpeed)
		{
			interpSpeed = Time.deltaTime * 5;
			switch (stateHash)
			{
				case StartToSitDown:
				case SittingIdle:
					return settings.SittingIdleOffset;

				case SittingChair:
					return settings.SittingOnChairOffset;

				case SittingThrone:
					return settings.SittingOnThroneOffset;
				
				case SittingShip:
					return settings.SittingOnShipOffset;
				
				case HoldingMast:
					return settings.HoldingMastOffset;
				
				case HoldingDragon:
					return settings.HoldingDragonOffset;
				
				case Sleeping:
					return settings.SleepingOffset;
				
				default:
					interpSpeed = 1;
					return Vector3.zero;
			}
		}
		
		void LateUpdate()
		{
			float playerScaleFactor = settings.PlayerHeight / 1.85f;
			
			vrmAnim.transform.localPosition = Vector3.zero;
			
			orgPose.GetHumanPose(ref hp);
			vrmPose.SetHumanPose(ref hp);

			var curStateHash = orgAnim.GetCurrentAnimatorStateInfo(0).shortNameHash;
			var nextState = orgAnim.GetNextAnimatorStateInfo(0);
			var nextStateHash = nextState.shortNameHash;

			//if (newStateHash != prevHash)
			//{
			//	prevHash = newStateHash;
			//	Debug.Log(orgAnim.GetCurrentAnimatorClipInfo(0)[0].clip.name + ": " + newStateHash);
			//}

			var vrmHip = vrmAnim.GetBoneTransform(HumanBodyBones.Hips);
			var orgHip = orgAnim.GetBoneTransform(HumanBodyBones.Hips);
			
			vrmHip.position = orgAnim.GetBoneTransform(HumanBodyBones.Hips).position;

			Vector3 actualAdjustHipPos;
			float actualInterpSpeed;

			//---------
			
			var curAdjustPos = Vector3.zero;

			if (!adjustHipHashes.Contains(curStateHash))
			{
				Vector3 curOrgHipPos = orgHip.position - orgHip.parent.position;
				Vector3 curVrmHipPos = curOrgHipPos * playerScaleFactor;
				
				curAdjustPos = curVrmHipPos - curOrgHipPos;
			}

			float curInterpSpeed = Time.deltaTime * 5;
			Vector3 curOffset = StateHashToOffset(curStateHash, out curInterpSpeed);
			if (curOffset != Vector3.zero) curAdjustPos += orgHip.transform.rotation * curOffset;
			
			//---------
			
			var nextAdjustPos = Vector3.zero;

			if (nextStateHash != 0)
			{
				if (!adjustHipHashes.Contains(nextStateHash))
				{
					Vector3 nextOrgHipPos = orgHip.position - orgHip.parent.position;
					Vector3 nextVrmHipPos = nextOrgHipPos * playerScaleFactor;
				
					nextAdjustPos = nextVrmHipPos - nextOrgHipPos;
				}

				float nextInterpSpeed = Time.deltaTime * 5;
				Vector3 nextOffset = StateHashToOffset(nextStateHash, out nextInterpSpeed);
				if (nextOffset != Vector3.zero) nextAdjustPos += orgHip.transform.rotation * nextOffset;

				float trans = Mathf.Clamp01(nextState.normalizedTime * nextState.length / 0.5f);

				actualInterpSpeed = Mathf.Lerp(curInterpSpeed, nextInterpSpeed, trans);

				actualAdjustHipPos = Vector3.Lerp(curAdjustPos, nextAdjustPos, trans);
			}
			else
			{
				actualInterpSpeed = curInterpSpeed;

				actualAdjustHipPos = curAdjustPos;
			}

			//---------
			
			adjustPos = adjustPos.HasValue ? Vector3.Lerp(adjustPos.Value, actualAdjustHipPos, actualInterpSpeed) : curAdjustPos;
			
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

			oldStateHash = curStateHash;
		}
	}
}
