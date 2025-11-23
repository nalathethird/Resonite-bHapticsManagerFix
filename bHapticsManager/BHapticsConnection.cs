using System;
using System.Collections.Generic;
using System.Linq;
using Elements.Core;
using FrooxEngine;
using ResoniteModLoader;
using ModernBHaptics = bHapticsLib;
using LegacyBHaptics = Bhaptics.Tact;

namespace bHapticsManager {
	
	public class BHapticsConnection {
		private static BHapticsConnection _instance = null!;
		public static BHapticsConnection Instance => _instance ??= new BHapticsConnection();
		
		public static readonly Dictionary<ModernBHaptics.PositionID, (bool isActive, DateTime lastCheck)> DeviceCache = new();
		
		private static ModernBHapticsWorkerThread _workerThread = null!;
		private static bool _isInitialized = false;
		private static readonly object _initializeLock = new();
		private static readonly object _shutdownLock = new();
		private static bool _isShuttingDown = false;

		// Events for device connection/disconnection - used by DeviceEventHandler
		public event Action<LegacyBHaptics.PositionType>? DeviceConnected;
		public event Action<LegacyBHaptics.PositionType>? DeviceDisconnected;

		// Event raising methods
		internal void RaiseDeviceConnected(LegacyBHaptics.PositionType position) {
			DeviceConnected?.Invoke(position);
		}

		internal void RaiseDeviceDisconnected(LegacyBHaptics.PositionType position) {
			DeviceDisconnected?.Invoke(position);
		}

		/// Initializes connection to bHaptics Player and subscribes to events.
		/// Called once during mod initialization.
		
		public static bool Initialize() {
			lock (_initializeLock) {
				if (_isInitialized) {
					ResoniteMod.Warn("Already initialized - skipping duplicate connection");
					return true;
				}
				
				// Connect to bHaptics Player
				bool connected = ModernBHaptics.bHapticsManager.Connect("Resonite", "Resonite", true, 10);
				
				if (!connected) {
					ResoniteMod.Error("Failed to connect to bHaptics Player!");
					ResoniteMod.Error("Make sure bHaptics Player is running and try restarting Resonite.");
					return false;
				}

				_isInitialized = true;
				ResoniteMod.Msg("bHapticsManager connected successfully!");
				
				// Log connected devices
				int deviceCount = ModernBHaptics.bHapticsManager.GetConnectedDeviceCount();
				ResoniteMod.Msg($"Connected devices: {deviceCount}");
				
				foreach (ModernBHaptics.PositionID pos in Enum.GetValues(typeof(ModernBHaptics.PositionID))) {
					if (ModernBHaptics.bHapticsManager.IsDeviceConnected(pos)) {
						ResoniteMod.Debug($"Device {pos} ready");
						// Add to cache
						DeviceCache[pos] = (true, DateTime.Now);
					}
				}

				return true;
			}
		}

		public List<LegacyBHaptics.PositionType> GetConnectedDevices() {
			var connectedDevices = new List<LegacyBHaptics.PositionType>();
			
			foreach (ModernBHaptics.PositionID pos in Enum.GetValues(typeof(ModernBHaptics.PositionID))) {
				if (ModernBHaptics.bHapticsManager.IsDeviceConnected(pos)) {
					var legacyPos = PositionMapper.MapModernToLegacy(pos);
					if (!connectedDevices.Contains(legacyPos)) {
						connectedDevices.Add(legacyPos);
					}
				}
			}
			
			return connectedDevices;
		}
		
		
		/// Starts the worker thread after FrooxEngine has initialized all haptic points.
		/// This should be called AFTER BHapticsDriver.InitializeBhaptics() completes.
		
		public static void StartWorkerThread() {
			try {
				var engine = Engine.Current;
				if (engine == null) {
					ResoniteMod.Error("Engine not ready - cannot start worker thread");
					return;
				}
				
				var inputInterface = engine.InputInterface;
				if (inputInterface == null) {
					ResoniteMod.Error("InputInterface not ready - cannot start worker thread");
					return;
				}
				
				if (inputInterface.HapticPointCount == 0) {
					ResoniteMod.Warn($"No haptic points registered yet - worker thread will be idle");
				}
				
				if (_workerThread != null) {
					ResoniteMod.Warn("Worker thread already exists - skipping duplicate start");
					return;
				}
				
				_workerThread = new ModernBHapticsWorkerThread(inputInterface);
				_workerThread.Start();
				
				ResoniteMod.Debug("Worker thread started successfully");
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Failed to start worker thread: {ex.Message}");
			}
		}

		/// Shuts down the connection to bHaptics Player, stopping all patterns and clearing the device cache.
		
		public static void Shutdown() {
			lock (_shutdownLock) {
				if (_isShuttingDown) {
					ResoniteMod.Warn("Shutdown already in progress");
					return;
				}
				_isShuttingDown = true;
			}
			
			try {
				ResoniteMod.Debug("Starting bHaptics connection shutdown...");
				
				// Stop worker thread first
				if (_workerThread != null) {
					try {
						ResoniteMod.Debug("Stopping worker thread...");
						_workerThread.Stop();
						_workerThread = null!;
						ResoniteMod.Debug("Worker thread stopped");
					}
					catch (Exception ex) {
						ResoniteMod.Error($"Error stopping worker thread: {ex}");
					}
				}
				
				// Stop all haptic playback
				try {
					ResoniteMod.Debug("Stopping all haptic playback...");
					ModernBHaptics.bHapticsManager.StopPlayingAll();
					ResoniteMod.Debug("Haptic playback stopped");
				}
				catch (Exception ex) {
					ResoniteMod.Error($"Error stopping haptic playback: {ex}");
				}
				
				// Small delay to ensure all patterns are stopped
				System.Threading.Thread.Sleep(100);
				
				// Disconnect from bHaptics Player
				try {
					ResoniteMod.Debug("Disconnecting from bHaptics Player...");
					bool disconnected = ModernBHaptics.bHapticsManager.Disconnect();
					
					if (disconnected) {
						ResoniteMod.Debug("Disconnected from bHaptics Player successfully");
					} else {
						ResoniteMod.Warn("Disconnect returned false - may already be disconnected");
					}
				}
				catch (Exception ex) {
					ResoniteMod.Error($"Error disconnecting from bHaptics Player: {ex}");
				}
				
				// Clear caches
				try {
					DeviceCache.Clear();
					ResoniteMod.Debug("Device cache cleared");
				}
				catch (Exception ex) {
					ResoniteMod.Error($"Error clearing device cache: {ex}");
				}
				
				lock (_initializeLock) {
					_isInitialized = false;
				}
				ResoniteMod.Msg("bHaptics connection shutdown complete");
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error during bHaptics connection shutdown: {ex}");
			}
			finally {
				lock (_shutdownLock) {
					_isShuttingDown = false;
				}
			}
		}
		
		
		/// Invalidates device cache for a specific device (called when device status changes)
		
		public static void InvalidateDeviceCache(ModernBHaptics.PositionID position, bool isConnected) {
			DeviceCache[position] = (isConnected, DateTime.Now);
		}
	}
}
