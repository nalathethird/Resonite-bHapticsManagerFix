using System;
using System.Collections.Generic;
using Elements.Core;
using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using System.Reflection;
using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	
	public static class HapticMethodPatches {
		public static void ApplyPatches(Harmony harmony) {
			try {
				harmony.PatchAll(typeof(IsActivePatch).Assembly);
				ResoniteMod.Debug("HapticMethodPatches applied successfully");
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Failed to apply HapticMethodPatches: {ex}");
			}
		}
	}
	
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "IsActive")]
	public class IsActivePatch {
		static bool Prefix(LegacyBHaptics.PositionType type, ref bool __result) {
			if (ModernBHaptics.bHapticsManager.Status != ModernBHaptics.bHapticsStatus.Connected) {
				__result = false;
				return false;
			}

			try {
				var position = PositionMapper.MapLegacyToModern(type);
				
				var cache = BHapticsConnection.DeviceCache;
				if (cache.TryGetValue(position, out var cached)) {
					if ((DateTime.Now - cached.lastCheck).TotalMilliseconds < bHapticsManager.DEVICE_CHECK_CACHE_MS) {
						__result = cached.isActive;
						return false;
					}
				}

				bool isActive = ModernBHaptics.bHapticsManager.IsDeviceConnected(position);
				
				cache[position] = (isActive, DateTime.Now);
				__result = isActive;
				
				return false;
			}
			catch (Exception ex) {
				ResoniteMod.Error($"IsActive check failed: {ex.Message}");
				__result = false;
				return false;
			}
		}
	}

	[HarmonyPatch(typeof(HapticPointData), "OnCommonUpdate")]
	public class HapticPointDataUpdatePatch {
		private static readonly HashSet<int> _registeredRemoteSources = new();
		private static DateTime _lastCleanup = DateTime.Now;
		private const int CLEANUP_INTERVAL_MS = 1000;
		private static int _updateCount = 0;
		private static DateTime _lastDiagnostic = DateTime.Now;

		static bool Prefix(HapticPointData __instance) {
			try {
				if ((DateTime.Now - _lastCleanup).TotalMilliseconds > CLEANUP_INTERVAL_MS) {
					RemoteHapticSource.CleanupStaleData();
					_lastCleanup = DateTime.Now;
				}
				
				int index = __instance.Index.Value;
				if (index < 0 || index >= __instance.InputInterface.HapticPointCount) {
					return false;
				}

				HapticPoint point = __instance.InputInterface.GetHapticPoint(index);
				if (point == null) {
					return false;
				}

				var user = __instance.User.Target;
				var localUser = __instance.LocalUser;
				bool isUserspace = __instance.World.IsUserspace();
				bool enableSelfHaptics = bHapticsManager.Config?.GetValue(bHapticsManager.ENABLE_SELF_HAPTICS) ?? false;
				bool isLocalUser = user == localUser;
				bool isRemoteUser = !isUserspace && user != null && !isLocalUser;
				bool isUnownedData = user == null;
				
				bool isDiagnosticEnabled = bHapticsManager.Config?.GetValue(bHapticsManager.ENABLE_DIAGNOSTIC_LOGGING) ?? false;
				if (isDiagnosticEnabled) {
					bool hasActivity = point.Force > 0f || point.Pain > 0f || point.Vibration > 0f || point.Temperature != 0f;
					_updateCount++;
					
					if ((DateTime.Now - _lastDiagnostic).TotalSeconds >= 10 || (hasActivity && _updateCount % 100 == 0)) {
						ResoniteMod.Debug($"[HapticData#{index}] user={user?.UserName ?? "null"} isLocal={isLocalUser} isRemote={isRemoteUser} unowned={isUnownedData} selfEnabled={enableSelfHaptics} activity={hasActivity}");
						_lastDiagnostic = DateTime.Now;
					}
				}
				
				if (isRemoteUser) {
					float remoteForce = __instance.Force.Value;
					float remoteTemp = __instance.Temperature.Value;
					float remotePain = __instance.Pain.Value;
					float remoteVib = __instance.Vibration.Value;
					
					bool hasRemoteData = remoteForce > 0f || remoteTemp != 0f || remotePain > 0f || remoteVib > 0f;
					
					if (hasRemoteData) {
						RemoteHapticSource.UpdateRemoteData(index, remoteForce, remoteTemp, remotePain, remoteVib);
						
						if (!_registeredRemoteSources.Contains(index)) {
							RemoteHapticSource.RegisterForPoint(point, __instance.World);
							_registeredRemoteSources.Add(index);
							
							if (isDiagnosticEnabled) {
								ResoniteMod.Debug($"Registered RemoteHapticSource for point {index}");
							}
						}
					} else {
						RemoteHapticSource.ClearRemoteData(index);
					}
					
					return false;
				}
				else if (isLocalUser) {
					if (enableSelfHaptics) {
						__instance.Force.Value = point.Force;
						__instance.Temperature.Value = point.Temperature;
						__instance.Pain.Value = point.Pain;
						__instance.Vibration.Value = point.Vibration;
						__instance.TotalActivationIntensity.Value = point.TotalActivationIntensity;
						
						if (isDiagnosticEnabled && (point.Force > 0f || point.Pain > 0f)) {
							ResoniteMod.Debug($"Self-haptics active for point {index}: F={point.Force:F2} P={point.Pain:F2}");
						}
						
						return false;
					} else {
						__instance.Force.Value = 0f;
						__instance.Temperature.Value = 0f;
						__instance.Pain.Value = 0f;
						__instance.Vibration.Value = 0f;
						__instance.TotalActivationIntensity.Value = 0f;
						
						return false;
					}
				}
				else if (isUnownedData || isUserspace) {
					__instance.Force.Value = point.Force;
					__instance.Temperature.Value = point.Temperature;
					__instance.Pain.Value = point.Pain;
					__instance.Vibration.Value = point.Vibration;
					__instance.TotalActivationIntensity.Value = point.TotalActivationIntensity;
					
					return false;
				}

				return false;
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in HapticPointData.OnCommonUpdate patch: {ex.Message}");
				return true;
			}
		}
	}

	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "Submit", new Type[] { 
		typeof(string), 
		typeof(LegacyBHaptics.PositionType), 
		typeof(List<LegacyBHaptics.DotPoint>), 
		typeof(int) 
	})]
	public class SubmitPatch {
		private static int _submitCallCount = 0;
		
		static bool Prefix(string key, LegacyBHaptics.PositionType position, List<LegacyBHaptics.DotPoint> points, int durationMillis) {
			_submitCallCount++;
			
			if (_submitCallCount % 1000 == 0) {
				LegacyCompatibilityLayer.CleanupOldSubmissions();
			}
			
			if (ModernBHaptics.bHapticsManager.Status != ModernBHaptics.bHapticsStatus.Connected) {
				return false;
			}
			
			LegacyCompatibilityLayer.SubmitFrame(key, position, points, durationMillis);
			
			return false;
		}
	}
}
