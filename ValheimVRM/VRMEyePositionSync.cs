using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ValheimVRM
{
	public class VRMEyePositionSync : MonoBehaviour
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
