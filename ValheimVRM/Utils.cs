using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
			if (self == null) return "none";
			
			StringBuilder hex = new StringBuilder(self.Count() * 2);
			foreach (byte b in self) hex.AppendFormat("{0:x2}", b);
			return hex.ToString();
		}

		public static V GetOrCreateDefault<K, V>(this IDictionary<K, V> self, K key) where V : new() 
		{
			if (self.ContainsKey(key)) return self[key];

			var newVal = new V();
			self[key] = newVal;
			return newVal;
		}

		public static bool CompareArrays<T>(IEnumerable<T> a, IEnumerable<T> b) => ((a == null) == (b == null)) &&
			((a != null && b != null) ? Enumerable.SequenceEqual(a, b) : true);


		public static FieldInfo GetField<T>(string name) =>
			typeof(T).GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
		
		public static MethodInfo GetMethod<T>(string name) =>
			typeof(T).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

		public static int FindOp(this List<CodeInstruction> self, OpCode code, int from = 0) =>
			self.FindIndex(from, inst => inst.opcode == code);
		public static int FindOp(this List<CodeInstruction> self, OpCode code, object operand, int from = 0) =>
			self.FindIndex(from, inst => inst.opcode == code && inst.operand.Equals(operand));
		
		public static bool IsOp(CodeInstruction self, OpCode code) =>
			self.opcode == code;
		public static bool IsOp(this CodeInstruction self, OpCode code, object operand) =>
			self.opcode == code && self.operand.Equals(operand);
	}
}