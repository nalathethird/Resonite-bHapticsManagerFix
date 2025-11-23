using Elements.Core;
using FrooxEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ResoniteModLoader;
using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {

	public static class DeviceRegistration {
		private static readonly object _lock = new object();
		private static readonly HashSet<ModernBHaptics.PositionID> _registeredDevices = new HashSet<ModernBHaptics.PositionID>();
		private static readonly Dictionary<ModernBHaptics.PositionID, Task> _pendingRegistrations = new();
		private static readonly Dictionary<ModernBHaptics.PositionID, List<HapticPoint>> _devicePoints = new Dictionary<ModernBHaptics.PositionID, List<HapticPoint>>();
		
		private static InputInterface? _inputInterface;
		private static BHapticsDriver? _bhapticsDriver;
		
		private static readonly object _registrationLock = new object();

		public static void RegisterDevice(LegacyBHaptics.PositionType position) {
			var modernPos = PositionMapper.MapLegacyToModern(position);
			TryRegisterDevice(modernPos);
		}

		public static void UnregisterDevice(LegacyBHaptics.PositionType position) {
			var modernPos = PositionMapper.MapLegacyToModern(position);
			UnregisterDevice(modernPos);
		}

		public static void ClearAllRegistrations() {
			lock (_registrationLock) {
				_registeredDevices.Clear();
				_pendingRegistrations.Clear();
				_devicePoints.Clear();
			}
		}

		public static Task<bool> TryRegisterDeviceAsync(ModernBHaptics.PositionID position) {
			lock (_registrationLock) {
				if (_registeredDevices.Contains(position)) {
					return Task.FromResult(true);
				}
				
				if (_pendingRegistrations.TryGetValue(position, out Task? existingTask)) {
					return existingTask as Task<bool> ?? Task.FromResult(false);
				}
				
				var task = Task.Run(async () => {
					try {
						return await RegisterDeviceInternalAsync(position);
					}
					finally {
						lock (_registrationLock) {
							_pendingRegistrations.Remove(position);
						}
					}
				});
				
				_pendingRegistrations[position] = task;
				return task;
			}
		}
		
		public static bool TryRegisterDevice(ModernBHaptics.PositionID position) {
			return TryRegisterDeviceAsync(position).Result;
		}
		
		private static async Task<bool> RegisterDeviceInternalAsync(ModernBHaptics.PositionID position) {
			try {
				for (int i = 0; i < 50; i++) {
					if (Engine.Current != null && Engine.Current.InputInterface != null) {
						break;
					}
					await Task.Delay(100);
				}
				
				if (!EnsureFrooxEngineReferences()) {
					ResoniteMod.Warn($"Engine not ready for {position} registration - will retry on next connection");
					return false;
				}
				
				var legacyPosition = PositionMapper.MapModernToLegacy(position);

				ResoniteMod.Debug($"Registering haptic points for device: {position}");
				
				await Task.Delay(100);
				
				bool success = await Task.Run(() => CallInitializeMethod(legacyPosition));
				
				if (success) {
					lock (_registrationLock) {
						_registeredDevices.Add(position);
					}
					ResoniteMod.Debug($"Registered {position} successfully");
					
					await RefreshHapticPointSamplersAsync();
				}
				
				return success;
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Failed to register {position}: {ex.Message}");
				return false;
			}
		}

		public static async Task UnregisterDeviceAsync(ModernBHaptics.PositionID position) {
			try {
				lock (_registrationLock) {
					_registeredDevices.Remove(position);
				}
				
				Task? pendingTask;
				lock (_registrationLock) {
					_pendingRegistrations.TryGetValue(position, out pendingTask);
				}
				
				if (pendingTask != null) {
					await pendingTask;
				}
				
				await Task.Delay(50);
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error unregistering {position}: {ex.Message}");
			}
		}
		
		public static void UnregisterDevice(ModernBHaptics.PositionID position) {
			UnregisterDeviceAsync(position).Wait();
		}

		private static bool EnsureFrooxEngineReferences() {
			if (_inputInterface != null && _bhapticsDriver != null)
				return true;

			_inputInterface = Engine.Current?.InputInterface;
			
			if (_inputInterface == null) {
				return false;
			}
			
			if (_bhapticsDriver == null) {
				var driverField = typeof(InputInterface).GetField("inputDrivers", 
					BindingFlags.NonPublic | BindingFlags.Instance);
				
				if (driverField != null) {
					var drivers = driverField.GetValue(_inputInterface) as List<IInputDriver>;
					_bhapticsDriver = drivers?.OfType<BHapticsDriver>().FirstOrDefault();
				}
			}
			
			if (_bhapticsDriver == null) {
				return false;
			}

			return true;
		}

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
						
					case LegacyBHaptics.PositionType.HandL:
						return InvokeMethod("InitializeHand", true);
						
					case LegacyBHaptics.PositionType.HandR:
						return InvokeMethod("InitializeHand", false);
						
					case LegacyBHaptics.PositionType.FootL:
						return InvokeMethod("InitializeFoot", true);
						
					case LegacyBHaptics.PositionType.FootR:
						return InvokeMethod("InitializeFoot", false);
						
					default:
						ResoniteMod.Warn($"No initialization method for position type: {position}");
						return false;
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Failed to call initialization method for {position}: {ex.Message}");
				return false;
			}
		}

		private static bool InvokeMethod(string methodName, bool? leftSide = null) {
			try {
				var method = typeof(BHapticsDriver).GetMethod(methodName, 
					BindingFlags.NonPublic | BindingFlags.Instance);
				
				if (method == null) {
					ResoniteMod.Error($"Could not find method {methodName}");
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
				
				if (innerEx.Message.Contains("already") || innerEx.Message.Contains("duplicate")) {
					return true;
				}
				
				ResoniteMod.Error($"Failed to invoke {methodName}: {innerEx.Message}");
				return false;
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Failed to invoke {methodName}: {ex.Message}");
				return false;
			}
		}

		private static async Task RefreshHapticPointSamplersAsync() {
			try {
				var engine = Engine.Current;
				if (engine == null) {
					return;
				}

				int totalRefreshed = 0;
				var refreshTasks = new List<Task>();

				foreach (var world in engine.WorldManager.Worlds.ToList()) {
					if (world == null || world.IsDestroyed || world.IsDisposed) continue;

					var task = Task.Run(() => {
						try {
							var samplers = new List<HapticPointSampler>();
							world.RunSynchronously(() => {
								world.GetGloballyRegisteredComponents(samplers);

								foreach (var sampler in samplers) {
									if (sampler == null || sampler.IsRemoved || sampler.IsDestroyed) continue;

									int currentIndex = sampler.HapticPointIndex.Value;
									sampler.HapticPointIndex.Value = -1;
									sampler.HapticPointIndex.Value = currentIndex;
								}
							});

							return samplers.Count;
						}
						catch (Exception ex) {
							ResoniteMod.Error($"Error refreshing samplers in world: {ex.Message}");
							return 0;
						}
					});
					
					refreshTasks.Add(task);
				}

				var results = await Task.WhenAll(refreshTasks.Select(t => (Task<int>)t));
				totalRefreshed = results.Sum();

				if (totalRefreshed > 0) {
					ResoniteMod.Debug($"Refreshed {totalRefreshed} haptic sampler(s)");
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in RefreshHapticPointSamplersAsync: {ex.Message}");
			}
		}
		
		public static int GetRegisteredDeviceCount() {
			lock (_registrationLock) {
				return _registeredDevices.Count;
			}
		}
		
		public static int GetPendingRegistrationCount() {
			lock (_registrationLock) {
				return _pendingRegistrations.Count;
			}
		}
	}
}
