using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValheimVRM
{
	public static class Utils
	{
		public static Tout GetField<Tin, Tout>(this Tin self, string fieldName)
		{
			return AccessTools.FieldRefAccess<Tin, Tout>(fieldName).Invoke(self);
		}

		public static string GetHaxadecimalString(this IEnumerable<byte> self)
		{
			StringBuilder hex = new StringBuilder(self.Count() * 2);
			foreach (byte b in self) hex.AppendFormat("{0:x2}", b);
			return hex.ToString();
		}
	}
}