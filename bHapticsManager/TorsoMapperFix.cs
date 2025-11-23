using HarmonyLib;
using Elements.Core;
using FrooxEngine;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Reflection.Emit;
using ResoniteModLoader;

namespace bHapticsManager {
	
	public static class TorsoMapperFix {
		public static void ApplyPatches(Harmony harmony) {
			try {
				harmony.PatchAll(typeof(TorsoMapperFix));
				ResoniteMod.Debug("TorsoMapperFix patches applied successfully");
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Failed to apply TorsoMapperFix: {ex}");
			}
		}
	}
	
	[HarmonyPatch(typeof(CommonAvatarBuilder), "BuildAvatar")]
	public static class CommonAvatarBuilderPatch {
		
		private static bool _torsoMapperPatchApplied = false;
		
		static void Postfix(UserRoot userRoot) {
			try {
				if (!_torsoMapperPatchApplied) {
					var harmony = new Harmony("com.nalathethird.bHapticsManager.TorsoMapper");
					ApplyTorsoMapperPatch(harmony);
					_torsoMapperPatchApplied = true;
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in BuildAvatar postfix: {ex.Message}");
			}
		}
		
		private static void ApplyTorsoMapperPatch(Harmony harmony) {
			try {
				var frooxEngineAssembly = typeof(FrooxEngine.Engine).Assembly;
				Type? torsoMapperType = frooxEngineAssembly.GetType("FrooxEngine.TorsoHapticPointMapper", false)
					?? frooxEngineAssembly.GetType("TorsoHapticPointMapper", false);
				
				if (torsoMapperType == null) {
					ResoniteMod.Warn("Could not find TorsoHapticPointMapper type");
					return;
				}
				
				var mapPointsMethod = torsoMapperType.GetMethod("MapPoints", 
					BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
					null,
					new Type[] { 
						frooxEngineAssembly.GetType("FrooxEngine.HapticManager")!,
						typeof(float),
						typeof(Span<float>)
					},
					null);
				
				if (mapPointsMethod == null) {
					mapPointsMethod = torsoMapperType.GetMethod("MapPoints", 
						BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
					
					if (mapPointsMethod == null) {
						ResoniteMod.Warn($"Could not find MapPoints method in {torsoMapperType.FullName}");
						return;
					}
				}
				
				Type? slotPositioningType = frooxEngineAssembly.GetType("FrooxEngine.SlotPositioning", false)
					?? frooxEngineAssembly.GetType("SlotPositioning", false);
				
				if (slotPositioningType == null) {
					ResoniteMod.Warn("Could not find SlotPositioning type");
					return;
				}
				
				var getClosestAxisMethod = slotPositioningType.GetMethod("GetClosestAxis",
					BindingFlags.Public | BindingFlags.Static,
					null,
					new Type[] { typeof(Slot), typeof(float3).MakeByRefType() },
					null);
				
				if (getClosestAxisMethod == null) {
					getClosestAxisMethod = slotPositioningType.GetMethod("GetClosestAxis",
						BindingFlags.Public | BindingFlags.Static);
				}
				
				if (getClosestAxisMethod == null) {
					ResoniteMod.Warn("Could not find GetClosestAxis method");
					return;
				}
				
				var transpiler = typeof(TorsoMapperFixTranspiler).GetMethod(
					nameof(TorsoMapperFixTranspiler.ReplaceGetClosestAxis), 
					BindingFlags.Public | BindingFlags.Static);
				
				if (transpiler == null) {
					ResoniteMod.Error("Could not find transpiler method");
					return;
				}
				
				TorsoMapperFixTranspiler.TargetMethod = getClosestAxisMethod;
				
				harmony.Patch(mapPointsMethod, transpiler: new HarmonyMethod(transpiler));
				
				ResoniteMod.Msg("TorsoHapticPointMapper rotation fix applied successfully");
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error applying TorsoMapper patch: {ex.Message}");
			}
		}
	}
	
	public static class TorsoMapperFixTranspiler {
		
		public static MethodInfo TargetMethod = null!;
		
		public static float3 IdentityGetClosestAxis(Slot slot, float3 direction) {
			return direction;
		}
		
		public static float3 IdentityGetClosestAxisByRef(Slot slot, ref float3 direction) {
			return direction;
		}
		
		public static IEnumerable<CodeInstruction> ReplaceGetClosestAxis(IEnumerable<CodeInstruction> instructions) {
			try {
				var codes = new List<CodeInstruction>(instructions);
				int patchedCount = 0;
				
				var identityMethod = typeof(TorsoMapperFixTranspiler).GetMethod(
					nameof(IdentityGetClosestAxis), 
					BindingFlags.Public | BindingFlags.Static);
				
				var identityMethodByRef = typeof(TorsoMapperFixTranspiler).GetMethod(
					nameof(IdentityGetClosestAxisByRef), 
					BindingFlags.Public | BindingFlags.Static);
				
				if (identityMethod == null || identityMethodByRef == null) {
					ResoniteMod.Error("Could not find identity methods!");
					return instructions;
				}
				
				for (int i = 0; i < codes.Count; i++) {
					var instruction = codes[i];
					
					if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && 
					    instruction.operand is MethodInfo method) {
						
						if (method == TargetMethod || 
						    (method.Name == "GetClosestAxis" && method.DeclaringType?.Name == "SlotPositioning")) {
							
							var parameters = method.GetParameters();
							bool useByRef = parameters.Length > 1 && parameters[1].ParameterType.IsByRef;
							
							codes[i] = new CodeInstruction(OpCodes.Call, useByRef ? identityMethodByRef : identityMethod);
							
							patchedCount++;
						}
					}
				}
				
				if (patchedCount > 0) {
					ResoniteMod.Debug($"Replaced {patchedCount} GetClosestAxis call(s)");
				} else {
					ResoniteMod.Warn("No GetClosestAxis calls found - method may have changed");
				}
				
				return codes;
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Transpiler error: {ex.Message}");
				return instructions;
			}
		}
	}
}
