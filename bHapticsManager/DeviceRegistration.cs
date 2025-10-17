// Handles dynamic registration of haptic points for hot-plugged devices

using Elements.Core;
using FrooxEngine;
using System.Reflection;

using ResoniteModLoader;

using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {

	/// Manages dynamic registration of haptic points when devices are connected.
	/// Uses reflection to access FrooxEngine's BHapticsDriver initialization methods.

	/// This enables hot-plug support - devices can be turned on/off without restarting Resonite - FINALLY!

	public static class DeviceRegistration {
		// Device tracking for whats already connected
		private static readonly HashSet<ModernBHaptics.PositionID> _registeredDevices = new();
		
		// Cached references to FrooxEngine for later uses
		private static InputInterface _inputInterface;
		private static BHapticsDriver _bhapticsDriver;


		/// Attempts to register haptic points for a newly connected device.
		/// Returns true if registration succeeded, false otherwise.

		public static bool TryRegisterDevice(ModernBHaptics.PositionID position) {
			// Skip if already registered
			if (_registeredDevices.Contains(position)) {
				return true;
			}

			try {
				// Get references to FrooxEngine internals
				if (!EnsureFrooxEngineReferences()) {
					return false;
				}
				
				// Map modern PositionID to legacy PositionType
				var legacyPosition = PositionMapper.MapModernToLegacy(position);

				ResoniteMod.Msg($" Registering haptic points for device: {position}");
				
				// Call the appropriate initialization method on BHapticsDriver
				bool success = CallInitializeMethod(legacyPosition);
				
				if (success) {
					_registeredDevices.Add(position);
					ResoniteMod.Msg($" Registered {position} successfully!");
					
					// Refresh all HapticPointSampler components
					RefreshHapticPointSamplers();
				}
				
				return success;
			}
			catch (Exception ex) {
				ResoniteMod.Error($" Failed to register {position}: {ex.Message}");
				return false;
			}
		}


		/// Marks a device as unregistered (called when device disconnects).

		public static void UnregisterDevice(ModernBHaptics.PositionID position) {
			_registeredDevices.Remove(position);
		}

		/// Ensures we have valid references to InputInterface and BHapticsDriver.
		/// Uses reflection to access FrooxEngine internals.

		private static bool EnsureFrooxEngineReferences() {
			if (_inputInterface != null && _bhapticsDriver != null)
				return true;

			_inputInterface = Engine.Current?.InputInterface;
			
			if (_inputInterface == null) {
				ResoniteMod.Warn("Cannot register points - InputInterface not available yet");
				return false;
			}
			
			if (_bhapticsDriver == null) {
				// Find the BHapticsDriver instance in the list of input drivers
				var driverField = typeof(InputInterface).GetField("inputDrivers", 
					BindingFlags.NonPublic | BindingFlags.Instance);
				
				if (driverField != null) {
					var drivers = driverField.GetValue(_inputInterface) as List<IInputDriver>;
					_bhapticsDriver = drivers?.OfType<BHapticsDriver>().FirstOrDefault();
				}
			}
			
			if (_bhapticsDriver == null) {
				ResoniteMod.Warn("Cannot register points - BHapticsDriver not found");
				return false;
			}

			return true;
		}


		/// Calls the appropriate initialization method on BHapticsDriver for the given device type.
		/// These methods (InitializeHead, InitializeVest, etc.) create HapticPoint objects.

		private static bool CallInitializeMethod(LegacyBHaptics.PositionType position) {
			try {
				switch (position) {
					case LegacyBHaptics.PositionType.Head:
						return InvokeMethod("InitializeHead");
						
					case LegacyBHaptics.PositionType.Vest:
					case LegacyBHaptics.PositionType.VestFront:
					case LegacyBHaptics.PositionType.VestBack:
						return InvokeMethod("InitializeVest");
						
					case LegacyBHaptics.PositionType.ForearmL:
						return InvokeMethod("InitializeForearm", true);
						
					case LegacyBHaptics.PositionType.ForearmR:
						return InvokeMethod("InitializeForearm", false);
						
					case LegacyBHaptics.PositionType.FootL:
						return InvokeMethod("InitializeFoot", true);
						
					case LegacyBHaptics.PositionType.FootR:
						return InvokeMethod("InitializeFoot", false);
						
					default:
						ResoniteMod.Warn($" No initialization method for position type: {position}");
						return false;
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($" Failed to call initialization method for {position}: {ex.Message}");
				return false;
			}
		}

		/// Invokes a private initialization method on BHapticsDriver using reflection.

		private static bool InvokeMethod(string methodName, bool? leftSide = null) {
			try {
				var method = typeof(BHapticsDriver).GetMethod(methodName, 
					BindingFlags.NonPublic | BindingFlags.Instance);
				
				if (method == null) {
					ResoniteMod.Error($" Could not find method {methodName}");
					return false;
				}
				
				if (leftSide.HasValue) {
					method.Invoke(_bhapticsDriver, new object[] { leftSide.Value });
				}
				else {
					method.Invoke(_bhapticsDriver, null);
				}
				
				return true;
			}
			catch (TargetInvocationException tex) {
				var innerEx = tex.InnerException ?? tex;
				
				// If points are already registered, treat as success
				if (innerEx.Message.Contains("already") || innerEx.Message.Contains("duplicate")) {
					return true;
				}
				
				ResoniteMod.Error($" Failed to invoke {methodName}: {innerEx.Message}");
				return false;
			}
			catch (Exception ex) {
				ResoniteMod.Error($" Failed to invoke {methodName}: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Refreshes all HapticPointSampler components across all worlds.
		/// This forces them to re-check InputInterface.HapticPointCount and update their _hapticPoint references.
		/// CRITICAL for hot-plug support - prevents NullReferenceException when entering HapticVolumes!
		/// </summary>
		private static void RefreshHapticPointSamplers() {
			try {
				var engine = Engine.Current;
				if (engine == null) {
					return;
				}

				int totalRefreshed = 0;

				foreach (var world in engine.WorldManager.Worlds) {
					if (world == null || world.IsDestroyed || world.IsDisposed) continue;

					world.RunSynchronously(() => {
						try {
							var samplers = new List<HapticPointSampler>();
							world.GetGloballyRegisteredComponents(samplers);

							foreach (var sampler in samplers) {
								if (sampler == null || sampler.IsRemoved || sampler.IsDestroyed) continue;

								// Force re-initialization
								int currentIndex = sampler.HapticPointIndex.Value;
								sampler.HapticPointIndex.Value = -1;
								sampler.HapticPointIndex.Value = currentIndex;
							}

							totalRefreshed += samplers.Count;
						}
						catch (Exception ex) {
							ResoniteMod.Error($" Error refreshing samplers: {ex.Message}");
						}
					});
				}

				if (totalRefreshed > 0) {
					ResoniteMod.Msg($" Refreshed {totalRefreshed} haptic sampler(s)");
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($" Error in RefreshHapticPointSamplers: {ex.Message}");
			}
		}
	}
}
