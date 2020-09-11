using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib.Internal.Patching;
using HarmonyLib.Public.Patching;
using MonoMod.Cil;

namespace HarmonyLib
{
	/// <summary>Patch function helpers</summary>
	internal static class PatchFunctions
	{
		/// <summary>Sorts patch methods by their priority rules</summary>
		/// <param name="original">The original method</param>
		/// <param name="patches">Patches to sort</param>
		/// <param name="debug">Use debug mode. Present for source parity with Harmony 2, don't use.</param>
		/// <returns>The sorted patch methods</returns>
		///
		internal static List<MethodInfo> GetSortedPatchMethods(MethodBase original, Patch[] patches, bool debug = false)
		{
			return new PatchSorter(patches, debug).Sort(original);
		}

		/// <summary>Creates new replacement method with the latest patches and detours the original method</summary>
		/// <param name="original">The original method</param>
		/// <param name="patchInfo">Information describing the patches</param>
		/// <returns>The newly created replacement method</returns>
		///
		internal static MethodInfo UpdateWrapper(MethodBase original, PatchInfo patchInfo)
		{
			var patcher = original.GetMethodPatcher();
			var dmd = patcher.PrepareOriginal();

			if (dmd != null)
			{
				var ctx = new ILContext(dmd.Definition);
				HarmonyManipulator.Manipulate(original, patchInfo, ctx);
			}

			try
			{
				return patcher.DetourTo(dmd?.Generate()) as MethodInfo;
			}
			catch (Exception ex)
			{
				Dictionary<int, CodeInstruction> finalInstructions = new Dictionary<int, CodeInstruction>();
				if (dmd != null)
				{
					var manipulator = new ILManipulator(dmd.Definition.Body);
					finalInstructions = manipulator.GetIndexedInstructions(PatchProcessor.CreateILGenerator());
				}
				throw HarmonyException.Create(ex, finalInstructions);
			}
		}

		internal static MethodInfo ReversePatch(HarmonyMethod standin, MethodBase original, MethodInfo postTranspiler)
		{
			if (standin is null)
				throw new ArgumentNullException(nameof(standin));
			if (standin.method is null)
				throw new ArgumentNullException($"{nameof(standin)}.{nameof(standin.method)}");

			var debug = (standin.debug ?? false) || Harmony.DEBUG;

			var transpilers = new List<MethodInfo>();
			if (standin.reversePatchType == HarmonyReversePatchType.Snapshot)
			{
				var info = Harmony.GetPatchInfo(original);
				transpilers.AddRange(GetSortedPatchMethods(original, info.Transpilers.ToArray(), debug));
			}
			if (postTranspiler is object) transpilers.Add(postTranspiler);

			var empty = new List<MethodInfo>();
			var patcher = new MethodPatcher(standin.method, original, empty, empty, transpilers, empty, debug);
			var replacement = patcher.CreateReplacement(out var finalInstructions);
			if (replacement is null) throw new MissingMethodException($"Cannot create replacement for {standin.method.FullDescription()}");

			try
			{
				var errorString = Memory.DetourMethod(standin.method, replacement);
				if (errorString is object)
					throw new FormatException($"Method {standin.method.FullDescription()} cannot be patched. Reason: {errorString}");
			}
			catch (Exception ex)
			{
				throw HarmonyException.Create(ex, finalInstructions);
			}

			PatchTools.RememberObject(standin.method, replacement);
			return replacement;
		}
	}
}
