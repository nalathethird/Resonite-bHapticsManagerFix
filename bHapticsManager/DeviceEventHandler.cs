// DeviceEventHandler.cs
// Handles all bHaptics device events (connect, disconnect, battery changes)

using Elements.Core;
using FrooxEngine;

using ResoniteModLoader;

using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	
	/// Central event handler for all bHaptics device events.
	/// Manages device status changes, connection state, and triggers device registration.
	
	public static class DeviceEventHandler {
		
		/// Subscribes to all bHaptics events (device status, connection, battery).
		
		public static void Subscribe() {
			ModernBHaptics.bHapticsManager.DeviceStatusChanged += OnDeviceStatusChanged;
			ModernBHaptics.bHapticsManager.ConnectionEstablished += OnConnectionEstablished;
			ModernBHaptics.bHapticsManager.ConnectionLost += OnConnectionLost;
			ModernBHaptics.bHapticsManager.StatusChanged += OnStatusChanged;
			
			// Battery monitoring is handled by BatteryMonitor.Initialize()
		}

		
		/// Called when any device connects or disconnects.
		/// Triggers hot-plug device registration for newly connected devices.
		/// NOTE: This is called from a BACKGROUND THREAD! Must defer to main thread for FrooxEngine access.
		
		public static void OnDeviceStatusChanged(object sender, ModernBHaptics.DeviceStatusChangedEventArgs e) {
			try {
				ResoniteMod.Msg($"Device {e.Position} {(e.IsConnected ? "CONNECTED" : "DISCONNECTED")} at {e.Timestamp:HH:mm:ss}");
				
				// CRITICAL: Engine must be initialized
				var engine = FrooxEngine.Engine.Current;
				if (engine == null) {
					ResoniteMod.Warn($"Engine not ready - skipping event handling");
					return;
				}
				
				// CRITICAL: Config must be initialized
				var config = bHapticsManager.Config;
				if (config == null) {
					ResoniteMod.Warn($"Config not ready - skipping event handling");
					return;
				}
				
				if (e.IsConnected) {
					// Device connected - reset idle state so it can receive data immediately
					var legacyPosition = PositionMapper.MapModernToLegacy(e.Position);
					LegacyCompatibilityLayer.ResetDevice(legacyPosition);
					
					if (config.GetValue(bHapticsManager.ENABLE_HOTPLUG)) {
						engine.WorkProcessor.Enqueue(() => {
							try {
								DeviceRegistration.TryRegisterDevice(e.Position);
							}
							catch (Exception ex) {
								ResoniteMod.Error($"Error registering device {e.Position}: {ex.Message}");
							}
						}, WorkType.Background);
					}
					else {
						ResoniteMod.Warn($"Device {e.Position} connected, but hot-plug is disabled.");
					}
					
					ResoniteMod.Msg($"Device {e.Position} ready - reset idle state");
				}
				else {
					// Device disconnected - clean up everything
					DeviceRegistration.UnregisterDevice(e.Position);
					
					// Stop all patterns on this device
					try {
						ModernBHaptics.bHapticsManager.StopPlayingAll();
					} catch { }
					
					// Clean up legacy compatibility tracking
					var legacyPosition = PositionMapper.MapModernToLegacy(e.Position);
					LegacyCompatibilityLayer.CleanupDevice(legacyPosition);
					
					ResoniteMod.Warn($" Device {e.Position} disconnected - cleaned up");
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in OnDeviceStatusChanged: {ex.Message}");
				ResoniteMod.Error($"Stack: {ex.StackTrace}");
			}
		}

		
		/// Called when connection to bHaptics Player is established (or re-established).
		
		public static void OnConnectionEstablished(object sender, EventArgs e) {
			try {
				ResoniteMod.Msg("Connection to bHaptics Player ESTABLISHED!");
				var deviceCount = ModernBHaptics.bHapticsManager.GetConnectedDeviceCount();
				ResoniteMod.Msg($"Detected {deviceCount} device(s)");
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in OnConnectionEstablished: {ex.Message}");
			}
		}

		
		/// Called when connection to bHaptics Player is lost.
		
		public static void OnConnectionLost(object sender, EventArgs e) {
			try {
				ResoniteMod.Warn("Connection to bHaptics Player LOST!");
				ResoniteMod.Warn("Haptics will not work until connection is re-established.");
				if (bHapticsManager.AUTO_RECONNECT) {
					ResoniteMod.Warn("Auto-reconnect is enabled - waiting for reconnection...");
				}
			}
			catch (Exception ex) {
				ResoniteMod.Error($" Error in OnConnectionLost: {ex.Message}");
			}
		}

		
		/// Called whenever connection status changes (Disconnected -> Connecting -> Connected).
		/// Only logs in debug mode to reduce spam.
		
		public static void OnStatusChanged(object sender, ModernBHaptics.ConnectionStatusChangedEventArgs e) {
			try {
				// No debug logging - always silent
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error in OnStatusChanged: {ex.Message}");
			}
		}
	}
}
