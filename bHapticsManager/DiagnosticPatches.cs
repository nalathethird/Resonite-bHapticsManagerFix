using System;
using System.Collections.Generic;
using HarmonyLib;
using FrooxEngine;
using ResoniteModLoader;
using ModernBHaptics = bHapticsLib;
using LegacyBHaptics = Bhaptics.Tact;

namespace bHapticsManager {
	[HarmonyPatch]
	public static class DiagnosticPatches {
		
		[HarmonyPatch(typeof(BHapticsDriver), "InitializeBhaptics")]
		public class BHapticsDriverInitPatch {
			static void Postfix(BHapticsDriver __instance) {
				if (!bHapticsManager.Config?.GetValue(bHapticsManager.ENABLE_DIAGNOSTIC_LOGGING) ?? false) {
					return;
				}
				
				try {
					ResoniteMod.Debug("=== bHaptics Device Detection ===");
					
					foreach (ModernBHaptics.PositionID pos in Enum.GetValues(typeof(ModernBHaptics.PositionID))) {
						bool connected = ModernBHaptics.bHapticsManager.IsDeviceConnected(pos);
						if (connected) {
							var legacy = PositionMapper.MapModernToLegacy(pos);
							ResoniteMod.Debug($"[OK] {pos} (Legacy: {legacy}) - CONNECTED");
						}
					}
					
					bool vestBackModern = ModernBHaptics.bHapticsManager.IsDeviceConnected(ModernBHaptics.PositionID.VestBack);
					bool vestBackLegacy = ModernBHaptics.bHapticsManager.IsDeviceConnected(
						PositionMapper.MapLegacyToModern(LegacyBHaptics.PositionType.VestBack)
					);
					
					ResoniteMod.Debug($"VestBack check: Modern={vestBackModern}, Legacy mapped={vestBackLegacy}");
					ResoniteMod.Debug("=================================");
				}
				catch (Exception ex) {
					ResoniteMod.Error($"Error in device detection diagnostic: {ex.Message}");
				}
			}
		}
		
		[HarmonyPatch(typeof(HapticPoint), "SampleSources")]
		public class HapticPointSampleDiagnosticPatch {
			private static DateTime _lastLog = DateTime.MinValue;
			private static readonly Dictionary<int, (float force, float temp, float pain, float vib)> _lastValues = new();
			
			static void Postfix(HapticPoint __instance) {
				if (!bHapticsManager.Config?.GetValue(bHapticsManager.ENABLE_DIAGNOSTIC_LOGGING) ?? false) {
					return;
				}
				
				try {
					int index = __instance.Index;
					float force = __instance.Force;
					float temp = __instance.Temperature;
					float pain = __instance.Pain;
					float vib = __instance.Vibration;
					
					bool changed = false;
					if (_lastValues.TryGetValue(index, out var last)) {
						float deltaF = Math.Abs(force - last.force);
						float deltaT = Math.Abs(temp - last.temp);
						float deltaP = Math.Abs(pain - last.pain);
						float deltaV = Math.Abs(vib - last.vib);
						
						changed = deltaF > 0.1f || deltaT > 0.1f || deltaP > 0.1f || deltaV > 0.1f;
					} else {
						changed = force > 0 || temp != 0 || pain > 0 || vib > 0;
					}
					
					if (changed) {
						_lastValues[index] = (force, temp, pain, vib);
						
						if ((DateTime.Now - _lastLog).TotalSeconds > 2) {
							ResoniteMod.Debug($"[Haptic#{index}] F={force:F2} T={temp:F2} P={pain:F2} V={vib:F2} | Position={__instance.Position}");
							_lastLog = DateTime.Now;
						}
					}
				}
				catch {
				}
			}
		}
		
		[HarmonyPatch(typeof(DirectTagHapticSource), "GetIntensity")]
		public class DirectTagHapticSourceDiagnosticPatch {
			private static DateTime _lastLog = DateTime.MinValue;
			
			static void Postfix(DirectTagHapticSource __instance, SensationClass sensation, float __result) {
				if (!bHapticsManager.Config?.GetValue(bHapticsManager.ENABLE_DIAGNOSTIC_LOGGING) ?? false) {
					return;
				}
				
				try {
					if (__result > 0f && (DateTime.Now - _lastLog).TotalSeconds > 2) {
						ResoniteMod.Debug($"[DirectTag] Tag='{__instance.HapticTag.Value}' Sensation={sensation} Intensity={__result:F2}");
						_lastLog = DateTime.Now;
					}
				}
				catch {
				}
			}
		}
	}
}
