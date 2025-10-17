// bHapticsManagerFix.cs
// Resonite mod to fix bHaptics initialization issues on .NET 9
// Author: NalaTheThird
//
// This mod solves the crash caused by the legacy bHaptics SDK using unsupported
// BeginInvoke/EndInvoke methods in .NET 9. It intercepts all bHaptics calls and
// routes them through a modernized SDK (bHapticsLib) that uses proper async/await patterns.
//
// Key Features:
// - Hot-plug device support (devices can connect/disconnect without restarting)
// - Event-driven device detection (no polling)
// - Device status caching (prevents frame drops)
// - Automatic reconnection on Player crashes
// - Full backward compatibility with FrooxEngine's haptics system

using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using SkyFrost.Base;
using System.Reflection;

// Alias the two conflicting namespaces for clarity
using LegacyBHaptics = Bhaptics.Tact;   // The old, broken SDK that ships with Resonite (v2025.9.23.1237)
using ModernBHaptics = bHapticsLib;     // My modernized fork that works on .NET 9 (see https://github.com/nalathethird/bHapticsLib)

public class bHapticsManagerFix : ResoniteMod 
{
    internal const string VERSION_CONSTANT = "1.1.0";
    public override string Name => "bHapticsManagerFix";
    public override string Author => "NalaTheThird";
    public override string Version => VERSION_CONSTANT;
    public override string Link => "https://github.com/nalathethird/Resonite-bHapticsSDK2Patch";

    private static ModConfiguration? Config;
    private static ModernBHaptics.bHapticsConnection? _connection;
    
    // Track which devices we've registered haptic points for (prevents duplicate registration)
    private static readonly HashSet<ModernBHaptics.PositionID> _registeredDevices = new();
    
    // Cached references to FrooxEngine internals (for dynamic device registration)
    private static InputInterface? _inputInterface;
    private static BHapticsDriver? _bhapticsDriver;

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> ENABLE_DEBUG = 
        new ModConfigurationKey<bool>("enable_debug", "Enable debug logging", () => false);

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<int> CONNECTION_TIMEOUT_MS = 
        new ModConfigurationKey<int>("connection_timeout_ms", "Connection timeout in milliseconds", () => 10000);

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> AUTO_RECONNECT = 
        new ModConfigurationKey<bool>("auto_reconnect", "Automatically reconnect on connection loss", () => true);

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<int> MAX_RETRIES = 
        new ModConfigurationKey<int>("max_retries", "Maximum reconnection attempts (0 = infinite)", () => 10);

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<int> DEVICE_CHECK_CACHE_MS = 
        new ModConfigurationKey<int>("device_check_cache_ms", "Device status cache duration in milliseconds (to prevent frame drops)", () => 1000);

    // Cache device status to avoid hammering bHaptics Player with status queries every frame
    // This prevents performance drops when multiple haptic points check device status simultaneously
    private static readonly Dictionary<ModernBHaptics.PositionID, (bool isActive, DateTime lastCheck)> _deviceCache = new();

    public override void DefineConfiguration(ModConfigurationDefinitionBuilder builder)
    {
        builder
            .Version(new Version(1, 0, 10))
            .AutoSave(true);
    }

    public override void OnEngineInit()
    {
        Config = GetConfiguration();
        bool debugEnabled = Config.GetValue(ENABLE_DEBUG);
        
        Msg("=== bHapticsManagerFix Initialization Starting ===");
        
        try 
        {
            var autoReconnect = Config.GetValue(AUTO_RECONNECT);
            var maxRetries = Config.GetValue(MAX_RETRIES);
            
            if (debugEnabled)
                Msg($"Config: autoReconnect={autoReconnect}, maxRetries={maxRetries}");
            
            // Quick port check - verify bHaptics Player is actually running
            bool playerRunning = false;
            try
            {
                using var testSocket = new System.Net.Sockets.TcpClient();
                testSocket.Connect("127.0.0.1", 15881);
                playerRunning = true;
                if (debugEnabled)
                    Msg("bHaptics Player detected on port 15881");
            }
            catch (Exception ex)
            {
                Error($"bHaptics Player is NOT running! Exception: {ex.Message}");
                Warn("Haptics will not work until bHaptics Player is started.");
            }
            
            // Use bHapticsManager (static API) for the main connection
            // This is simpler than managing bHapticsConnection instances ourselves
            if (debugEnabled)
                Msg("Calling bHapticsManager.Connect()...");
            
            bool managerConnected = ModernBHaptics.bHapticsManager.Connect("Resonite", "Resonite", autoReconnect, maxRetries);
            
            // Subscribe to device events for hot-plug support
            Msg("Subscribing to device status events...");
            ModernBHaptics.bHapticsManager.DeviceStatusChanged += OnDeviceStatusChanged;
            ModernBHaptics.bHapticsManager.ConnectionEstablished += OnConnectionEstablished;
            ModernBHaptics.bHapticsManager.ConnectionLost += OnConnectionLost;
            ModernBHaptics.bHapticsManager.StatusChanged += OnStatusChanged;
            
            if (debugEnabled)
            {
                Msg($"bHapticsManager.Connect() returned: {managerConnected}");
                Msg($"Status: {ModernBHaptics.bHapticsManager.Status}");
                Msg("Waiting 3 seconds for async connection...");
            }
            
            // Give the background thread time to establish WebSocket connection
            Thread.Sleep(3000);
            
            var managerStatus = ModernBHaptics.bHapticsManager.Status;
            
            if (managerStatus == ModernBHaptics.bHapticsStatus.Connected)
            {
                Msg("bHapticsManager connected successfully!");
                var deviceCount = ModernBHaptics.bHapticsManager.GetConnectedDeviceCount();
                Msg($"Connected devices: {deviceCount}");
                
                // List all currently connected devices
                if (deviceCount > 0)
                {
                    foreach (ModernBHaptics.PositionID pos in Enum.GetValues(typeof(ModernBHaptics.PositionID)))
                    {
                        if (ModernBHaptics.bHapticsManager.IsDeviceConnected(pos))
                        {
                            Msg($"  - {pos} device ready");
                        }
                    }
                }
            }
            else
            {
                Error($"bHapticsManager failed to connect! Status: {managerStatus}");
                Error("Haptics will not work. Check bHaptics Player and device connections.");
            }
            
            // ALSO create a bHapticsConnection instance for fallback
            // This is kept for future extensibility (e.g., connecting to remote bHaptics Player)
            if (debugEnabled)
                Msg("Creating bHapticsConnection instance as fallback...");
            
            _connection = new ModernBHaptics.bHapticsConnection("Resonite", "Resonite", autoReconnect, maxRetries);
            
            // Subscribe to connection events on the fallback too
            _connection.DeviceStatusChanged += OnDeviceStatusChanged;
            _connection.ConnectionEstablished += OnConnectionEstablished;
            _connection.ConnectionLost += OnConnectionLost;
            _connection.StatusChanged += OnStatusChanged;
            
            if (debugEnabled)
                Msg($"bHapticsConnection created with status: {_connection.Status}");
        }
        catch (Exception ex)
        {
            Error($"EXCEPTION during initialization: {ex.Message}");
            if (debugEnabled)
            {
                Error($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                    Error($"Inner exception: {ex.InnerException.Message}");
            }
        }

        // Apply Harmony patches to intercept legacy HapticPlayer calls
        Harmony harmony = new Harmony("nalathethird.bhapticsmanagerfix.com");
        harmony.PatchAll();
        Msg($"bHapticsManagerFix v{VERSION_CONSTANT} loaded successfully");
        Msg("=== Initialization Complete ===");
    }

    #region Event Handlers

    // Called when any device connects or disconnects
    private static void OnDeviceStatusChanged(object sender, ModernBHaptics.DeviceStatusChangedEventArgs e)
    {
        try
        {
            Msg($"Device {e.Position} {(e.IsConnected ? "CONNECTED" : "DISCONNECTED")} at {e.Timestamp:HH:mm:ss}");
            
            if (e.IsConnected)
            {
                // Hot-plug support: Register haptic points for newly connected device
                if (!_registeredDevices.Contains(e.Position))
                {
                    TryRegisterDeviceHapticPoints(e.Position);
                }
            }
            else
            {
                // Device disconnected - mark it as unregistered so it can be re-registered later
                _registeredDevices.Remove(e.Position);
                Warn($"Device {e.Position} disconnected. Haptic points will no longer respond.");
                Warn("Note: Points are not automatically removed (FrooxEngine limitation).");
            }
        }
        catch (Exception ex)
        {
            Error($"Error in OnDeviceStatusChanged: {ex.Message}");
        }
    }

    // Called when connection to bHaptics Player is established (or re-established)
    private static void OnConnectionEstablished(object sender, EventArgs e)
    {
        try
        {
            Msg("Connection to bHaptics Player ESTABLISHED!");
            var deviceCount = ModernBHaptics.bHapticsManager.GetConnectedDeviceCount();
            Msg($"Detected {deviceCount} device(s)");
        }
        catch (Exception ex)
        {
            Error($"Error in OnConnectionEstablished: {ex.Message}");
        }
    }

    // Called when connection to bHaptics Player is lost
    private static void OnConnectionLost(object sender, EventArgs e)
    {
        try
        {
            Warn("Connection to bHaptics Player LOST!");
            Warn("Haptics will not work until connection is re-established.");
            if (Config?.GetValue(AUTO_RECONNECT) == true)
            {
                Warn("Auto-reconnect is enabled - waiting for reconnection...");
            }
        }
        catch (Exception ex)
        {
            Error($"Error in OnConnectionLost: {ex.Message}");
        }
    }

    // Called whenever connection status changes (Disconnected -> Connecting -> Connected)
    private static void OnStatusChanged(object sender, ModernBHaptics.ConnectionStatusChangedEventArgs e)
    {
        try
        {
            if (Config?.GetValue(ENABLE_DEBUG) == true)
                Msg($"Connection status: {e.PreviousStatus} -> {e.NewStatus}");
        }
        catch (Exception ex)
        {
            Error($"Error in OnStatusChanged: {ex.Message}");
        }
    }

    #endregion

    #region Dynamic Device Registration

    /// <summary>
    /// Attempts to dynamically register haptic points for a newly connected device.
    /// Uses reflection to access FrooxEngine's InputInterface and BHapticsDriver,
    /// then calls the appropriate initialization method to create haptic points.
    /// 
    /// This enables hot-plug support - devices can be turned on/off without restarting Resonite!
    /// </summary>
    private static void TryRegisterDeviceHapticPoints(ModernBHaptics.PositionID position)
    {
        try
        {
            // Get references to FrooxEngine internals via reflection
            if (_inputInterface == null || _bhapticsDriver == null)
            {
                _inputInterface = Engine.Current?.InputInterface;
                
                if (_inputInterface != null)
                {
                    // Find the BHapticsDriver instance in the list of input drivers
                    var driverField = typeof(InputInterface).GetField("inputDrivers", 
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    if (driverField != null)
                    {
                        var drivers = driverField.GetValue(_inputInterface) as List<IInputDriver>;
                        _bhapticsDriver = drivers?.OfType<BHapticsDriver>().FirstOrDefault();
                    }
                }
            }
            
            if (_inputInterface == null)
            {
                Warn($"Cannot register points for {position} - InputInterface not available yet");
                return;
            }
            
            if (_bhapticsDriver == null)
            {
                Warn($"Cannot register points for {position} - BHapticsDriver not found");
                return;
            }
            
            // Map modern PositionID to legacy PositionType for calling FrooxEngine methods
            var legacyPosition = MapModernToLegacyPosition(position);
            
            Msg($"Registering haptic points for newly connected device: {position}");
            
            // Call the appropriate private initialization method on BHapticsDriver
            // These methods create HapticPoint objects and register them with InputInterface
            switch (legacyPosition)
            {
                case LegacyBHaptics.PositionType.Head:
                    CallInitializeMethod("InitializeHead");
                    break;
                    
                case LegacyBHaptics.PositionType.Vest:
                case LegacyBHaptics.PositionType.VestFront:
                case LegacyBHaptics.PositionType.VestBack:
                    CallInitializeMethod("InitializeVest");
                    break;
                    
                case LegacyBHaptics.PositionType.ForearmL:
                    CallInitializeMethod("InitializeForearm", true);
                    break;
                    
                case LegacyBHaptics.PositionType.ForearmR:
                    CallInitializeMethod("InitializeForearm", false);
                    break;
                    
                case LegacyBHaptics.PositionType.FootL:
                    CallInitializeMethod("InitializeFoot", true);
                    break;
                    
                case LegacyBHaptics.PositionType.FootR:
                    CallInitializeMethod("InitializeFoot", false);
                    break;
            }
            
            _registeredDevices.Add(position);
            Msg($"Successfully registered haptic points for {position}!");
            
        }
        catch (Exception ex)
        {
            Error($"Failed to register haptic points for {position}: {ex.Message}");
            if (Config?.GetValue(ENABLE_DEBUG) == true)
                Error($"Stack: {ex.StackTrace}");
        }
    }
    
    /// <summary>
    /// Invokes a private initialization method on BHapticsDriver using reflection.
    /// These methods (InitializeHead, InitializeVest, etc.) create the actual HapticPoint objects.
    /// </summary>
    private static void CallInitializeMethod(string methodName, bool? leftSide = null)
    {
        try
        {
            var method = typeof(BHapticsDriver).GetMethod(methodName, 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (method == null)
            {
                Error($"Could not find method {methodName} on BHapticsDriver");
                return;
            }
            
            if (leftSide.HasValue)
            {
                // Method takes a bool parameter (for left vs right sided devices)
                method.Invoke(_bhapticsDriver, new object[] { leftSide.Value });
            }
            else
            {
                // Method takes no parameters
                method.Invoke(_bhapticsDriver, null);
            }
        }
        catch (Exception ex)
        {
            Error($"Failed to call {methodName}: {ex.Message}");
        }
    }

    #endregion

    #region Position Mapping Helpers

    // Map modern PositionID back to legacy PositionType
    private static LegacyBHaptics.PositionType MapModernToLegacyPosition(ModernBHaptics.PositionID modernPos)
    {
        return modernPos switch
        {
            ModernBHaptics.PositionID.Head => LegacyBHaptics.PositionType.Head,
            ModernBHaptics.PositionID.Vest => LegacyBHaptics.PositionType.Vest,
            ModernBHaptics.PositionID.VestFront => LegacyBHaptics.PositionType.VestFront,
            ModernBHaptics.PositionID.VestBack => LegacyBHaptics.PositionType.VestBack,
            ModernBHaptics.PositionID.ArmLeft => LegacyBHaptics.PositionType.ForearmL,
            ModernBHaptics.PositionID.ArmRight => LegacyBHaptics.PositionType.ForearmR,
            ModernBHaptics.PositionID.HandLeft => LegacyBHaptics.PositionType.HandL,
            ModernBHaptics.PositionID.HandRight => LegacyBHaptics.PositionType.HandR,
            ModernBHaptics.PositionID.FootLeft => LegacyBHaptics.PositionType.FootL,
            ModernBHaptics.PositionID.FootRight => LegacyBHaptics.PositionType.FootR,
            _ => LegacyBHaptics.PositionType.Vest
        };
    }

    // Map legacy PositionType to modern PositionID
    private static ModernBHaptics.PositionID MapLegacyToModernPosition(LegacyBHaptics.PositionType legacyType)
    {
        return legacyType switch
        {
            LegacyBHaptics.PositionType.Head => ModernBHaptics.PositionID.Head,
            LegacyBHaptics.PositionType.Vest => ModernBHaptics.PositionID.Vest,
            LegacyBHaptics.PositionType.VestFront => ModernBHaptics.PositionID.VestFront,
            LegacyBHaptics.PositionType.VestBack => ModernBHaptics.PositionID.VestBack,
            LegacyBHaptics.PositionType.ForearmL => ModernBHaptics.PositionID.ArmLeft,
            LegacyBHaptics.PositionType.ForearmR => ModernBHaptics.PositionID.ArmRight,
            LegacyBHaptics.PositionType.HandL => ModernBHaptics.PositionID.HandLeft,
            LegacyBHaptics.PositionType.HandR => ModernBHaptics.PositionID.HandRight,
            LegacyBHaptics.PositionType.FootL => ModernBHaptics.PositionID.FootLeft,
            LegacyBHaptics.PositionType.FootR => ModernBHaptics.PositionID.FootRight,
            _ => ModernBHaptics.PositionID.Vest
        };
    }

    // Convert legacy DotPoint list to modern DotPoint list
    private static List<ModernBHaptics.DotPoint>? ConvertLegacyToModernDotPoints(List<LegacyBHaptics.DotPoint>? legacyPoints)
    {
        if (legacyPoints == null || legacyPoints.Count == 0)
            return null;

        var modernPoints = new List<ModernBHaptics.DotPoint>(legacyPoints.Count);
        foreach (var legacy in legacyPoints)
        {
            modernPoints.Add(new ModernBHaptics.DotPoint(legacy.Index, legacy.Intensity));
        }
        return modernPoints;
    }

    #endregion

    #region Harmony Patches

    /// <summary>
    /// Blocks the legacy HapticPlayer constructor to prevent it from creating
    /// a broken WebSocketSender that uses unsupported BeginInvoke/EndInvoke.
    /// Our patches intercept all method calls, so the constructor doesn't need to run.
    /// </summary>
    [HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), MethodType.Constructor, new Type[] { typeof(string), typeof(string), typeof(Action<bool>), typeof(bool) })]
    class HapticPlayerConstructorPatch
    {
        static bool Prefix()
        {
            if (Config?.GetValue(ENABLE_DEBUG) == true)
                Msg("Blocking HapticPlayer constructor - using modern connection instead");
            
            return false; // Skip constructor - we don't need it!
        }
    }

    [HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), MethodType.Constructor, new Type[] { typeof(string), typeof(string), typeof(bool) })]
    class HapticPlayerConstructorPatch2
    {
        static bool Prefix()
        {
            if (Config?.GetValue(ENABLE_DEBUG) == true)
                Msg("Blocking HapticPlayer constructor (overload) - using modern connection instead");
            return false;
        }
    }

    /// <summary>
    /// Intercepts IsActive() calls and routes them to our modern connection.
    /// Uses caching to prevent performance drops from excessive device checks.
    /// </summary>
    [HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "IsActive")]
    class IsActivePatch
    {
        static bool Prefix(LegacyBHaptics.PositionType type, ref bool __result)
        {
            // Prefer bHapticsConnection, but fall back to bHapticsManager if needed
            bool useManager = _connection == null || _connection.Status != ModernBHaptics.bHapticsStatus.Connected;
            
            if (useManager && ModernBHaptics.bHapticsManager.Status != ModernBHaptics.bHapticsStatus.Connected)
            {
                if (Config?.GetValue(ENABLE_DEBUG) == true)
                    Warn($"IsActive({type}): No active connection available");
                __result = false;
                return false;
            }

            try
            {
                var position = MapLegacyToModernPosition(type);
                var cacheDurationMs = Config?.GetValue(DEVICE_CHECK_CACHE_MS) ?? 1000;

                // Check cache first to avoid excessive queries
                if (_deviceCache.TryGetValue(position, out var cached))
                {
                    if ((DateTime.Now - cached.lastCheck).TotalMilliseconds < cacheDurationMs)
                    {
                        __result = cached.isActive;
                        return false;
                    }
                }

                // Cache miss or expired - query device status
                bool isActive = useManager 
                    ? ModernBHaptics.bHapticsManager.IsDeviceConnected(position)
                    : _connection.IsDeviceConnected(position);
                
                _deviceCache[position] = (isActive, DateTime.Now);
                __result = isActive;
                
                if (Config?.GetValue(ENABLE_DEBUG) == true)
                    Msg($"IsActive({type}/{position}) = {isActive} (via {(useManager ? "bHapticsManager" : "bHapticsConnection")})");
                
                return false; // Skip original method
            }
            catch (Exception ex)
            {
                Error($"IsActive check failed: {ex.Message}");
                __result = false;
                return false;
            }
        }
    }

    /// <summary>
    /// Intercepts Submit() calls and routes them to our modern connection.
    /// This is the core method that actually sends haptic data to devices.
    /// Called every frame for every active haptic point!
    /// </summary>
    [HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "Submit", new Type[] { 
        typeof(string), 
        typeof(LegacyBHaptics.PositionType), 
        typeof(List<LegacyBHaptics.DotPoint>), 
        typeof(int) 
    })]
    class SubmitPatch
    {
        static bool Prefix(string key, LegacyBHaptics.PositionType position, List<LegacyBHaptics.DotPoint> points, int durationMillis)
        {
            // Prefer bHapticsConnection, but fall back to bHapticsManager if needed
            bool useManager = _connection == null || _connection.Status != ModernBHaptics.bHapticsStatus.Connected;
            
            if (useManager && ModernBHaptics.bHapticsManager.Status != ModernBHaptics.bHapticsStatus.Connected)
            {
                if (Config?.GetValue(ENABLE_DEBUG) == true)
                    Warn($"Cannot submit haptic '{key}' - no active connection");
                return false;
            }

            try
            {
                var modernPosition = MapLegacyToModernPosition(position);
                var modernPoints = ConvertLegacyToModernDotPoints(points);

                if (modernPoints != null && modernPoints.Count > 0)
                {
                    // Debug: Log point data to verify correct indices/intensities
                    if (Config?.GetValue(ENABLE_DEBUG) == true)
                    {
                        Msg($"Submitting to {position}/{modernPosition}:");
                        int sampleCount = Math.Min(5, modernPoints.Count);
                        for (int i = 0; i < sampleCount; i++)
                        {
                            var pt = modernPoints[i];
                            Msg($"  Point[{i}]: Index={pt.Index}, Intensity={pt.Intensity}");
                        }
                        if (modernPoints.Count > sampleCount)
                        {
                            Msg($"  ... and {modernPoints.Count - sampleCount} more points");
                        }
                    }
                    
                    // Send to the appropriate API
                    if (useManager)
                    {
                        ModernBHaptics.bHapticsManager.Play(key, durationMillis, modernPosition, modernPoints);
                    }
                    else
                    {
                        _connection.Play<List<ModernBHaptics.DotPoint>, List<ModernBHaptics.PathPoint>>(
                            key, durationMillis, modernPosition, modernPoints, 
                            new List<ModernBHaptics.PathPoint>(), ModernBHaptics.MirrorDirection.None);
                    }

                    if (Config?.GetValue(ENABLE_DEBUG) == true)
                        Msg($"Submitted haptic '{key}' to {modernPosition} ({modernPoints.Count} points, {durationMillis}ms) via {(useManager ? "bHapticsManager" : "bHapticsConnection")}");
                }

                return false; // Skip original method
            }
            catch (Exception ex)
            {
                Error($"Failed to submit haptic '{key}': {ex.Message}");
                if (Config?.GetValue(ENABLE_DEBUG) == true)
                    Error($"Stack: {ex.StackTrace}");
                return false;
            }
        }
    }

    /// <summary>
    /// Blocks the Dispose() call to prevent the legacy HapticPlayer from
    /// trying to dispose the (non-existent) WebSocketSender.
    /// We manage the connection lifecycle ourselves via bHapticsManager.
    /// </summary>
    [HarmonyPatch(typeof(LegacyBHaptics.HapticPlayer), "Dispose")]
    class DisposePatch
    {
        static bool Prefix()
        {
            if (Config?.GetValue(ENABLE_DEBUG) == true)
                Msg("Blocking HapticPlayer.Dispose - we manage connection lifecycle");
            return false;
        }
    }

    #endregion
}
