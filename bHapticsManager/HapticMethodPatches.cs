// HapticMethodPatches.cs
// Harmony patches that intercept HapticPlayer methods (IsActive, Submit)

using Elements.Core;

using HarmonyLib;

using ResoniteModLoader;

using FrooxEngine;
using System.Reflection;

using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	
	/// Intercepts IsActive() calls and routes them to our modern connection.
	/// Uses caching to prevent performance drops from excessive device checks.
	
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "IsActive")]
	public class IsActivePatch {
		static bool Prefix(LegacyBHaptics.PositionType type, ref bool __result) {
			// Prefer bHapticsConnection, but fall back to bHapticsManager if needed
			var conn = bHapticsManager.Connection;
			bool useManager = conn == null || conn.Status != ModernBHaptics.bHapticsStatus.Connected;
			
			if (useManager && ModernBHaptics.bHapticsManager.Status != ModernBHaptics.bHapticsStatus.Connected) {
				__result = false;
				return false;
			}

			try {
				var position = PositionMapper.MapLegacyToModern(type);
				
				// Check cache first to avoid excessive queries
				var cache = BHapticsConnection.DeviceCache;
				if (cache.TryGetValue(position, out var cached)) {
					if ((DateTime.Now - cached.lastCheck).TotalMilliseconds < bHapticsManager.DEVICE_CHECK_CACHE_MS) {
						__result = cached.isActive;
						return false;
					}
				}

				// Cache miss or expired - query device status
				bool isActive = useManager 
					? ModernBHaptics.bHapticsManager.IsDeviceConnected(position)
					: conn.IsDeviceConnected(position);
				
				cache[position] = (isActive, DateTime.Now);
				__result = isActive;
				
				return false;
			}
			catch (Exception ex) {
				ResoniteMod.Error($" IsActive check failed: {ex.Message}");
				__result = false;
				return false;
			}
		}
	}

	/// Patch HapticPointData.OnCommonUpdate to make synchronization bidirectional.
	/// When values are synced FROM remote users, write them TO the local HapticPoint.
	/// This allows remote haptic triggers to work properly.
	[HarmonyPatch(typeof(HapticPointData), "OnCommonUpdate")]
	public class HapticPointDataUpdatePatch {
		// Cached reflection properties for performance
		private static PropertyInfo _forceProp;
		private static PropertyInfo _tempProp;
		private static PropertyInfo _painProp;
		private static PropertyInfo _vibProp;
		
		// Cache remote values so SampleSources patch can use them
		internal static readonly Dictionary<int, (float force, float temp, float pain, float vib, DateTime timestamp)> RemoteValues = new();
		
		// Cleanup timer to prevent unbounded dictionary growth
		private static DateTime _lastCleanup = DateTime.Now;
		private const int CLEANUP_INTERVAL_MS = 1000; // Clean up every second

		static HapticPointDataUpdatePatch() {
			var hapticPointType = typeof(HapticPoint);
			_forceProp = hapticPointType.GetProperty("Force");
			_tempProp = hapticPointType.GetProperty("Temperature");
			_painProp = hapticPointType.GetProperty("Pain");
			_vibProp = hapticPointType.GetProperty("Vibration");
		}

		static bool Prefix(HapticPointData __instance) {
			try {
				// Periodic cleanup to prevent memory leak
				if ((DateTime.Now - _lastCleanup).TotalMilliseconds > CLEANUP_INTERVAL_MS) {
					CleanupStaleEntries();
					_lastCleanup = DateTime.Now;
				}
				
				// Get index and validate
				int index = __instance.Index.Value;
				if (index < 0 || index >= __instance.InputInterface.HapticPointCount) {
					return false;
				}

				HapticPoint point = __instance.InputInterface.GetHapticPoint(index);
				if (point == null) {
					return false;
				}

				// Determine if this is local or remote data
				var user = __instance.User.Target;
				var localUser = __instance.LocalUser;
				
				// Check if we're in userspace
				bool isUserspace = __instance.World.IsUserspace();
				
				// Check if self-haptics are enabled
				bool enableSelfHaptics = bHapticsManager.Config?.GetValue(bHapticsManager.ENABLE_SELF_HAPTICS) ?? false;
				
				// Remote data = User.Target exists AND it's not the local user
				bool isRemoteData = !isUserspace && user != null && user != localUser;
				
				// Local data = User.Target is null OR it's the local user
				bool isLocalData = !isUserspace && (user == null || (user == localUser && enableSelfHaptics));

				if (isRemoteData) {
					// REMOTE DATA: Write FROM sync fields TO local HapticPoint
					float syncedForce = __instance.Force.Value;
					float syncedTemp = __instance.Temperature.Value;
					float syncedPain = __instance.Pain.Value;
					float syncedVib = __instance.Vibration.Value;

					// Cache remote values
					RemoteValues[index] = (syncedForce, syncedTemp, syncedPain, syncedVib, DateTime.Now);
					
					// Inject values immediately
					_forceProp?.SetValue(point, syncedForce);
					_tempProp?.SetValue(point, syncedTemp);
					_painProp?.SetValue(point, syncedPain);
					_vibProp?.SetValue(point, syncedVib);
				}
				else if (isLocalData || isUserspace) {
					// LOCAL DATA or USERSPACE: Write FROM local HapticPoint TO sync fields
					// This includes DirectTagHapticSource from TipTouchSource!
					__instance.Force.Value = point.Force;
					__instance.Temperature.Value = point.Temperature;
					__instance.Pain.Value = point.Pain;
					__instance.Vibration.Value = point.Vibration;
					__instance.TotalActivationIntensity.Value = point.TotalActivationIntensity;
				}

				return false; // Skip original method
			}
			catch (Exception ex) {
				ResoniteMod.Error($" Error in HapticPointData.OnCommonUpdate patch: {ex.Message}");
				return true; // Run original method on error
			}
		}
		
		// Clean up stale entries older than 500ms
		private static void CleanupStaleEntries() {
			var now = DateTime.Now;
			var keysToRemove = new List<int>();
			
			foreach (var kvp in RemoteValues) {
				if ((now - kvp.Value.timestamp).TotalMilliseconds > 500) {
					keysToRemove.Add(kvp.Key);
				}
			}
			
			foreach (var key in keysToRemove) {
				RemoteValues.Remove(key);
			}
			
			// Silent cleanup - no logging
		}
	}

	/// Patch HapticPoint.SampleSources to COMPLETELY BYPASS sampling for remote values.
	/// This ensures remote values persist even if SampleSources tries to clear them.
	/// NOTE: For LOCAL haptics, we let SampleSources run normally!
	[HarmonyPatch(typeof(HapticPoint), "SampleSources")]
	public class HapticPointSampleSourcesPatch {
		// Use reflection to access the private properties
		private static PropertyInfo _forceProp;
		private static PropertyInfo _tempProp;
		private static PropertyInfo _painProp;
		private static PropertyInfo _vibProp;

		static HapticPointSampleSourcesPatch() {
			var hapticPointType = typeof(HapticPoint);
			_forceProp = hapticPointType.GetProperty("Force");
			_tempProp = hapticPointType.GetProperty("Temperature");
			_painProp = hapticPointType.GetProperty("Pain");
			_vibProp = hapticPointType.GetProperty("Vibration");
		}

		static bool Prefix(HapticPoint __instance) {
			try {
				int index = __instance.Index;
				
				// Check if we have cached REMOTE values for this point
				if (HapticPointDataUpdatePatch.RemoteValues.TryGetValue(index, out var cached)) {
					// Only use cached values if they're recent (within last 200ms)
					if ((DateTime.Now - cached.timestamp).TotalMilliseconds < 200) {
						// Only BYPASS if there's actual remote data (non-zero values)
						bool hasRemoteData = cached.force > 0f || cached.temp != 0f || cached.pain > 0f || cached.vib > 0f;
						
						if (hasRemoteData) {
							// We have REMOTE data - BYPASS SampleSources and use cached values
							_forceProp?.SetValue(__instance, cached.force);
							_tempProp?.SetValue(__instance, cached.temp);
							_painProp?.SetValue(__instance, cached.pain);
							_vibProp?.SetValue(__instance, cached.vib);

							return false; // SKIP SampleSources - we're using remote values!
						}
					}
					
					// Cached values are stale or zero, remove them
					HapticPointDataUpdatePatch.RemoteValues.Remove(index);
				}

				// No remote data (or it's stale/zero) - run normal SampleSources for LOCAL haptics
				return true; // Run original SampleSources for local data
			}
			catch (Exception ex) {
				ResoniteMod.Error($" Error in HapticPoint.SampleSources patch: {ex.Message}");
				return true; // Run original on error
			}
		}
	}

	
	/// Intercepts Submit() calls and routes them to our modern connection.
	/// This is the core method that actually sends haptic data to devices.
	/// Called every frame for every active haptic point!
	
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "Submit", new Type[] { 
		typeof(string), 
		typeof(LegacyBHaptics.PositionType), 
		typeof(List<LegacyBHaptics.DotPoint>), 
		typeof(int) 
	})]
	public class SubmitPatch {
		private static int _submitCallCount = 0;
		private static DateTime _lastLogTime = DateTime.MinValue;
		
		static bool Prefix(string key, LegacyBHaptics.PositionType position, List<LegacyBHaptics.DotPoint> points, int durationMillis) {
			_submitCallCount++;
			
			// Periodic cleanup every 1000 submissions
			if (_submitCallCount % 1000 == 0) {
				LegacyCompatibilityLayer.CleanupOldSubmissions();
			}
			
			// Check connection status
			var conn = bHapticsManager.Connection;
			bool useManager = conn == null || conn.Status != ModernBHaptics.bHapticsStatus.Connected;
			
			if (useManager && ModernBHaptics.bHapticsManager.Status != ModernBHaptics.bHapticsStatus.Connected) {
				return false;
			}
			
			// Use legacy-compatible submission (with rate limiting and extended duration)
			LegacyCompatibilityLayer.SubmitFrame(key, position, points, durationMillis);
			
			return false; // Skip original method
		}
	}
}
