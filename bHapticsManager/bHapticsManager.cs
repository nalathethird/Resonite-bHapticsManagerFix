using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using ResoniteModLoader;
using Elements.Core;
using FrooxEngine;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	public class bHapticsManager : ResoniteMod {
		internal const string VERSION_CONSTANT = "1.1.0";
		public override string Name => "bHapticsManager";
		public override string Author => "NalaTheThird";
		public override string Version => VERSION_CONSTANT;
		public override string Link => "https://github.com/nalathethird/bHapticsManager";

		public static ModConfiguration Config = null!;

		[AutoRegisterConfigKey]
		public static readonly ModConfigurationKey<bool> ENABLE_HOTPLUG =
			new("enable_hotplug", "Allow devices to connect/disconnect without restarting Resonite", () => true);

		[AutoRegisterConfigKey]
		public static readonly ModConfigurationKey<bool> ENABLE_SELF_HAPTICS =
			new("enable_self_haptics", "Allow your own touches to trigger your haptics (experimental)", () => false);

		[AutoRegisterConfigKey]
		public static readonly ModConfigurationKey<bool> ENABLE_DIAGNOSTIC_LOGGING =
			new("enable_diagnostic_logging", "Enable detailed diagnostic logging for debugging (causes spam)", () => false);

		internal const int CONNECTION_TIMEOUT_MS = 10000;
		internal const int DEVICE_CHECK_CACHE_MS = 1000;
		internal const int MAX_RETRIES = 10;
		internal const bool AUTO_RECONNECT = true;

		private static ModernBHapticsWorkerThread? _workerThread = null;
        private static DeviceEventHandler? _eventHandler = null;
        private static bool _initialized = false;
        private static bool _patchesApplied = false;
        private static bool _shutdownHookRegistered = false;

        public override void OnEngineInit()
        {
            try
            {
                if (_initialized)
                {
                    ResoniteMod.Debug("bHapticsManager already initialized, skipping");
                    return;
                }

                Config = GetConfiguration()!;
                
                if (Config == null)
                {
                    Error("Failed to get mod configuration");
                    return;
                }

                if (!_patchesApplied)
                {
                    try
                    {
                        Harmony harmony = new Harmony("com.bhaptics.resonite.fix");
                        
                        HapticPlayerPatches.ApplyPatches(harmony);
                        ResoniteMod.Debug("HapticPlayerPatches applied");
                        
                        HapticMethodPatches.ApplyPatches(harmony);
                        ResoniteMod.Debug("HapticMethodPatches applied");
                        
                        TorsoMapperFix.ApplyPatches(harmony);
                        ResoniteMod.Debug("TorsoMapperFix applied");
                        
                        LegacyCompatibilityLayer.ApplyPatches(harmony);
                        ResoniteMod.Debug("LegacyCompatibilityLayer applied");
                        
                        _patchesApplied = true;
                        Msg("All patches applied successfully");
                    }
                    catch (Exception ex)
                    {
                        Error($"Failed to apply patches: {ex}");
                    }
                }

                if (!BHapticsConnection.Initialize())
                {
                    Error("Failed to initialize bHaptics connection - is bHaptics Player running?");
                    return;
                }
                
                Msg("bHaptics connection initialized");

                var engine = Engine.Current;
                if (engine == null)
                {
                    Error("Engine.Current is null");
                    return;
                }

                if (!_shutdownHookRegistered)
                {
                    try
                    {
                        engine.OnShutdownRequest += OnEngineShutdown;
                        _shutdownHookRegistered = true;
                        ResoniteMod.Debug("Shutdown hook registered");
                    }
                    catch (Exception ex)
                    {
                        Error($"Failed to register shutdown hook: {ex}");
                    }
                }

                if (engine.WorldManager == null)
                {
                    Error("Engine.WorldManager is null");
                    return;
                }

                var focusedWorld = engine.WorldManager.FocusedWorld;
                if (focusedWorld == null)
                {
                    Warn("No focused world, initializing on next world focus");
                    engine.WorldManager.WorldFocused += (world) =>
                    {
                        if (world != null)
                        {
                            // Atomically set _initialized to true if it was false
                            if (Interlocked.CompareExchange(ref _initialized, true, false) == false)
                            {
                                world.RunSynchronously(() => InitializeHaptics());
                            }
                        }
                    };
                }
                else
                {
                    // Atomically set _initialized to true if it was false
                    if (Interlocked.CompareExchange(ref _initialized, true, false) == false)
                    {
                        focusedWorld.RunSynchronously(() => InitializeHaptics());
                    }
                }
                Msg("bHapticsManager initialized successfully");
            }
            catch (Exception ex)
            {
                Error($"Failed to initialize bHapticsManager: {ex}");
            }
        }

        private static void InitializeHaptics()
        {
            try
            {
                ResoniteMod.Debug("Initializing haptics system...");
                
                var connectedDevices = BHapticsConnection.Instance?.GetConnectedDevices();
                if (connectedDevices == null || connectedDevices.Count == 0)
                {
                    Warn("No bHaptics devices detected");
                    return;
                }
                
                Msg($"Detected {connectedDevices.Count} device(s)");

                var allPoints = new List<HapticPoint>();
                foreach (var position in connectedDevices)
                {
                    try
                    {
                        DeviceRegistration.RegisterDevice(position);
                        ResoniteMod.Debug($"Registered device: {position}");
                    }
                    catch (Exception ex)
                    {
                        Error($"Failed to register device {position}: {ex}");
                    }
                }

                var inputInterface = Engine.Current?.InputInterface;
                if (inputInterface == null)
                {
                    Error("InputInterface is null, cannot initialize haptics");
                    return;
                }

                int pointCount = inputInterface.HapticPointCount;
                ResoniteMod.Debug($"Found {pointCount} haptic points registered");
                
                for (int i = 0; i < pointCount; i++)
                {
                    var point = inputInterface.GetHapticPoint(i);
                    if (point != null)
                    {
                        allPoints.Add(point);
                    }
                }

                if (allPoints.Count == 0)
                {
                    Warn("No haptic points available");
                    return;
                }

                Msg($"Starting worker thread with {allPoints.Count} points across {connectedDevices.Count} devices");

                try
                {
                    _workerThread = new ModernBHapticsWorkerThread(allPoints);
                    ResoniteMod.Debug("Worker thread created successfully");
                }
                catch (Exception ex)
                {
                    Error($"Failed to create worker thread: {ex}");
                    return;
                }

                try
                {
                    _eventHandler = new DeviceEventHandler();
                    if (_workerThread != null)
                    {
                        _eventHandler.Initialize(_workerThread);
                        ResoniteMod.Debug("Event handlers subscribed successfully");
                    }
                }
                catch (Exception ex)
                {
                    Error($"Failed to initialize event handlers: {ex}");
                }
                
                Msg("Haptics system initialized successfully");
            }
            catch (Exception ex)
            {
                Error($"Error initializing haptics: {ex}");
            }
        }

        private static void OnEngineShutdown(string reason)
        {
            try
            {
                ResoniteMod.Debug($"Engine shutdown requested: {reason}");
                Msg("Starting bHapticsManager shutdown...");

                if (_eventHandler != null)
                {
                    try
                    {
                        ResoniteMod.Debug("Disposing event handler...");
                        _eventHandler.Dispose();
                        _eventHandler = null;
                        ResoniteMod.Debug("Event handler disposed");
                    }
                    catch (Exception ex)
                    {
                        Error($"Error disposing event handler: {ex}");
                    }
                }

                if (_workerThread != null)
                {
                    try
                    {
                        ResoniteMod.Debug("Disposing worker thread...");
                        _workerThread.Dispose();
                        _workerThread = null;
                        ResoniteMod.Debug("Worker thread disposed");
                    }
                    catch (Exception ex)
                    {
                        Error($"Error disposing worker thread: {ex}");
                    }
                }

                try
                {
                    ResoniteMod.Debug("Clearing device registrations...");
                    DeviceRegistration.ClearAllRegistrations();
                    ResoniteMod.Debug("Device registrations cleared");
                }
                catch (Exception ex)
                {
                    Error($"Error clearing device registrations: {ex}");
                }

                try
                {
                    ResoniteMod.Debug("Shutting down bHaptics connection...");
                    BHapticsConnection.Shutdown();
                    ResoniteMod.Debug("bHaptics connection shutdown complete");
                }
                catch (Exception ex)
                {
                    Error($"Error shutting down bHaptics connection: {ex}");
                }

                _initialized = false;
                Msg("bHapticsManager shutdown complete");
            }
            catch (Exception ex)
            {
                Error($"Critical error during shutdown: {ex}");
            }
        }

		public static void Msg(string message)
		{
			ResoniteMod.Msg($"[bHapticsManager] {message}");
		}

		public static void Warn(string message)
		{
			ResoniteMod.Warn($"[bHapticsManager] {message}");
		}

		public static void Error(string message)
		{
			ResoniteMod.Error($"[bHapticsManager] {message}");
		}

		public static void Error(Exception ex)
		{
			ResoniteMod.Error($"[bHapticsManager] {ex}");
		}
	}
}
