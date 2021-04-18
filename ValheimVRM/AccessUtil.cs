using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValheimVRM
{
	public static class AccessUtil
	{
		public static Tout GetField<Tin, Tout>(this Tin self, string fieldName)
		{
			return AccessTools.FieldRefAccess<Tin, Tout>(fieldName).Invoke(self);
		}
	}
}