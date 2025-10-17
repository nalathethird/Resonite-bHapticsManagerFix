// BHapticsConnection.cs
// Handles connection initialization to bHaptics Player

using Elements.Core;

using FrooxEngine;

using ResoniteModLoader;

using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	
	/// Manages the connection to bHaptics Player, including initialization,
	/// event subscription, and connection status monitoring.
	
	public static class BHapticsConnection {
		// Device status cache to prevent frame drops from excessive checks
		public static readonly Dictionary<ModernBHaptics.PositionID, (bool isActive, DateTime lastCheck)> DeviceCache = new();

		
		/// Initializes connection to bHaptics Player and subscribes to events.
		/// Called once during mod initialization.
		
		public static void Initialize() {
			ResoniteMod.Msg("Connection initialization starting...");
			
			// Connect to bHaptics Player
			bool connected = ModernBHaptics.bHapticsManager.Connect("Resonite", "Resonite", true, 10);
			
			if (!connected) {
				ResoniteMod.Error("Failed to connect to bHaptics Player!");
				ResoniteMod.Error("Make sure bHaptics Player is running and try restarting Resonite.");
				return;
			}

			// Subscribe to events
			ResoniteMod.Msg("Subscribing to device status events...");
			DeviceEventHandler.Subscribe();
			
			ResoniteMod.Msg("bHapticsManager connected successfully!");
			
			// Log connected devices
			int deviceCount = ModernBHaptics.bHapticsManager.GetConnectedDeviceCount();
			ResoniteMod.Msg($"Connected devices: {deviceCount}");
			
			foreach (ModernBHaptics.PositionID pos in Enum.GetValues(typeof(ModernBHaptics.PositionID))) {
				if (ModernBHaptics.bHapticsManager.IsDeviceConnected(pos)) {
					ResoniteMod.Msg($"{pos} device ready");
				}
			}
		}

		
		/// Checks if bHaptics Player is running by attempting to connect to port 15881.
		
		private static void CheckPlayerRunning(bool debugEnabled) {
			try {
				using var testSocket = new System.Net.Sockets.TcpClient();
				testSocket.Connect("127.0.0.1", 15881);
				if (debugEnabled)
					ResoniteMod.Msg("bHaptics Player detected on port 15881");
			}
			catch (Exception ex) {
				ResoniteMod.Error($"bHaptics Player is NOT running! Exception: {ex.Message}");
				ResoniteMod.Warn("Haptics will not work until bHaptics Player is started.");
			}
		}

		
		/// Logs all currently connected devices.
		
		private static void LogConnectedDevices() {
			var deviceCount = ModernBHaptics.bHapticsManager.GetConnectedDeviceCount();
			ResoniteMod.Msg($"Connected devices: {deviceCount}");
			
			if (deviceCount > 0) {
				foreach (ModernBHaptics.PositionID pos in Enum.GetValues(typeof(ModernBHaptics.PositionID))) {
					if (ModernBHaptics.bHapticsManager.IsDeviceConnected(pos)) {
						ResoniteMod.Msg($"{pos} device ready");
					}
				}
			}
		}
		
		
		/// Shuts down the connection to bHaptics Player, stopping all patterns and clearing the device cache.
		
		public static void Shutdown() {
			try {
				ResoniteMod.Msg("Disconnecting from bHaptics Player...");
				
				// Stop all patterns
				ModernBHaptics.bHapticsManager.StopPlayingAll();
				
				// Disconnect
				bool disconnected = ModernBHaptics.bHapticsManager.Disconnect();
				
				if (disconnected) {
					ResoniteMod.Msg("Disconnected successfully");
				} else {
					ResoniteMod.Warn("Disconnect returned false");
				}
				
				// Clear device cache
				DeviceCache.Clear();
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error during shutdown: {ex.Message}");
			}
		}
	}
}
