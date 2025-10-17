// HapticPlayerPatches.cs
// Harmony patches that block the legacy HapticPlayer constructor
// and intercept IsActive() to return modern device status

using Elements.Core;

using HarmonyLib;

using ResoniteModLoader;

using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	
	/// Blocks the legacy HapticPlayer constructor to prevent it from creating
	/// a broken WebSocketSender that uses unsupported BeginInvoke/EndInvoke.
	/// Our patches intercept all method calls, so the constructor doesn't need to run.
	
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), MethodType.Constructor, new Type[] { 
		typeof(string), typeof(string), typeof(Action<bool>), typeof(bool) 
	})]
	public class HapticPlayerConstructorPatch {
		static bool Prefix() {
			return false; // Skip constructor - we don't need it!
		}
	}

	
	/// Blocks the legacy HapticPlayer constructor (overload with fewer parameters).
	
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), MethodType.Constructor, new Type[] { 
		typeof(string), typeof(string), typeof(bool) 
	})]
	public class HapticPlayerConstructorPatch2 {
		static bool Prefix() {
			return false;
		}
	}

	
	/// Blocks the Dispose() call to prevent the legacy HapticPlayer from
	/// trying to dispose the (non-existent) WebSocketSender.
	/// We manage the connection lifecycle ourselves via bHapticsManager.
	
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "Dispose")]
	public class HapticPlayerDisposePatch {
		static bool Prefix() {
			return false;
		}
	}

	///  <summary>
	/// CRITICAL: Intercepts IsActive() to return modern device connection status!
	/// This is what FrooxEngine uses to determine which devices to initialize and send data to!
	/// Without this patch, FrooxEngine only initializes devices that were connected at startup.
	/// </summary>
	[HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "IsActive")]
	public class HapticPlayerIsActivePatch {
		static bool Prefix(LegacyBHaptics.PositionType type, ref bool __result) {
			try {
				// Map legacy position to modern
				var modernPosition = PositionMapper.MapLegacyToModern(type);
				
				// Check if device is connected using modern API
				__result = ModernBHaptics.bHapticsManager.IsDeviceConnected(modernPosition);
				
				return false; // Skip original method
			}
			catch {
				__result = false;
				return false;
			}
		}
	}

	/// <summary>
	/// Intercepts TurnOff(key) to immediately stop specific haptic patterns.
	/// </summary>
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

	/// <summary>
	/// Intercepts TurnOff() to immediately stop ALL haptic patterns.
	/// </summary>
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
