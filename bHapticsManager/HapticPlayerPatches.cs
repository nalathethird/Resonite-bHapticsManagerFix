using System;
using System.Linq;
using Elements.Core;
using HarmonyLib;
using ResoniteModLoader;
using System.Reflection;
using FrooxEngine;

using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	
	public static class HapticPlayerPatches {
		public static void ApplyPatches(Harmony harmony) {
			try {
				harmony.PatchAll(typeof(WebSocketSenderConstructorPatch).Assembly);
				ResoniteMod.Debug("HapticPlayerPatches applied successfully");
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Failed to apply HapticPlayerPatches: {ex}");
			}
		}
	}
	
	[HarmonyPatch]
	public class WebSocketSenderConstructorPatch {
		static MethodBase? TargetMethod() {
			try {
				var hapticPlayerType = typeof(LegacyBHaptics.HapticPlayer);
				
				Type? senderType = null;
				foreach (var name in new[] { "WebSocketSender", "WebSocketConnection", "Sender", "_sender" }) {
					senderType = hapticPlayerType.GetNestedType(name, BindingFlags.NonPublic | BindingFlags.Public);
					if (senderType != null) {
						ResoniteMod.Debug($"Found nested type: {name}");
						break;
					}
				}
				
				if (senderType == null) {
					var assembly = hapticPlayerType.Assembly;
					foreach (var type in assembly.GetTypes()) {
						if (type.Name.Contains("Sender") || type.Name.Contains("Socket")) {
							ResoniteMod.Debug($"Found potential sender type in assembly: {type.FullName}");
							senderType = type;
							break;
						}
					}
				}
				
				if (senderType == null) {
					ResoniteMod.Warn("Could not find WebSocketSender type - patch will be skipped");
					return null;
				}
				
				var constructor = senderType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).FirstOrDefault();
				if (constructor == null) {
					ResoniteMod.Warn($"Could not find constructor for {senderType.Name} - patch will be skipped");
					return null;
				}
				
				ResoniteMod.Debug($"Found and will patch {senderType.Name} constructor");
				return constructor;
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error finding WebSocketSender: {ex.Message}");
				return null;
			}
		}
		
		static bool Prefix() {
			ResoniteMod.Debug("Blocked WebSocketSender constructor (preventing duplicate connection)");
			return false;
		}
		
		static Exception? Finalizer(Exception? __exception) {
			if (__exception != null) {
				ResoniteMod.Debug("WebSocketSender constructor threw exception (expected - we blocked it)");
				return null;
			}
			return __exception;
		}
	}
	
	[HarmonyPatch(typeof(BHapticsDriver), "RegisterInputs")]
	public class BHapticsDriverRegisterInputsPatch {
		static void Postfix(BHapticsDriver __instance) {
			try {
				var workerField = typeof(BHapticsDriver).GetField("worker", BindingFlags.NonPublic | BindingFlags.Instance);
				if (workerField != null) {
					var worker = workerField.GetValue(__instance);
					if (worker != null) {
						var stopMethod = worker.GetType().GetMethod("Stop", BindingFlags.Public | BindingFlags.Instance);
						if (stopMethod != null) {
							stopMethod.Invoke(worker, null);
							ResoniteMod.Debug("Stopped FrooxEngine's BHapticsDriver worker thread");
						} else {
							ResoniteMod.Warn("Could not find Stop method on worker - trying Dispose");
							var disposeMethod = worker.GetType().GetMethod("Dispose", BindingFlags.Public | BindingFlags.Instance);
							if (disposeMethod != null) {
								disposeMethod.Invoke(worker, null);
								ResoniteMod.Debug("Disposed FrooxEngine's BHapticsDriver worker thread");
							}
						}
					} else {
						ResoniteMod.Debug("Worker field is null - FrooxEngine's worker didn't start");
					}
				} else {
					ResoniteMod.Warn("Could not find worker field on BHapticsDriver");
				}
			}
			catch (Exception ex) {
				ResoniteMod.Warn($"Could not stop BHapticsDriver worker: {ex.Message}");
			}
		}
	}
	
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), MethodType.Constructor, new Type[] { 
		typeof(string), typeof(string), typeof(Action<bool>), typeof(bool) 
	})]
	public class HapticPlayerConstructorPatch {
		static void Postfix(LegacyBHaptics.HapticPlayer __instance) {
			try {
				var senderField = typeof(LegacyBHaptics.HapticPlayer).GetField("_sender", 
					BindingFlags.NonPublic | BindingFlags.Instance);
				
				if (senderField != null) {
					senderField.SetValue(__instance, null);
					ResoniteMod.Debug("HapticPlayer constructed - forced _sender to null");
				} else {
					ResoniteMod.Warn("Could not find _sender field to nullify");
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in HapticPlayer constructor patch: {ex.Message}");
			}
		}
	}

	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), MethodType.Constructor, new Type[] { 
		typeof(string), typeof(string), typeof(bool) 
	})]
	public class HapticPlayerConstructorPatch2 {
		static void Postfix(LegacyBHaptics.HapticPlayer __instance) {
			try {
				var senderField = typeof(LegacyBHaptics.HapticPlayer).GetField("_sender", 
					BindingFlags.NonPublic | BindingFlags.Instance);
				
				if (senderField != null) {
					senderField.SetValue(__instance, null);
					ResoniteMod.Debug("HapticPlayer constructed (overload) - forced _sender to null");
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in HapticPlayer constructor patch (overload): {ex.Message}");
			}
		}
	}

	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "Dispose")]
	public class HapticPlayerDisposePatch {
		static bool Prefix() {
			return false;
		}
	}

	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "IsActive")]
	public class HapticPlayerIsActivePatch {
		static bool Prefix(LegacyBHaptics.PositionType type, ref bool __result) {
			try {
				var modernPosition = PositionMapper.MapLegacyToModern(type);
				__result = ModernBHaptics.bHapticsManager.IsDeviceConnected(modernPosition);
				return false;
			}
			catch {
				__result = false;
				return false;
			}
		}
	}
	
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "Submit", new Type[] { 
		typeof(string), 
		typeof(LegacyBHaptics.PositionType), 
		typeof(System.Collections.Generic.List<LegacyBHaptics.DotPoint>), 
		typeof(int) 
	})]
	public class HapticPlayerSubmitPatch {
		static bool Prefix() {
			return false;
		}
	}

	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "TurnOff", new Type[] { typeof(string) })]
	public class HapticPlayerTurnOffKeyPatch {
		static bool Prefix(string key) {
			try {
				ModernBHaptics.bHapticsManager.StopPlaying(key);
				return false;
			}
			catch {
				return false;
			}
		}
	}

	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "TurnOff", new Type[0])]
	public class HapticPlayerTurnOffAllPatch {
		static bool Prefix() {
			try {
				ModernBHaptics.bHapticsManager.StopPlayingAll();
				return false;
			}
			catch {
				return false;
			}
		}
	}
}
