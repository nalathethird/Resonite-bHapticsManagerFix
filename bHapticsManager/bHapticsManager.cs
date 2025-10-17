// Main entry point for the bHapticsManager "Mod"
// Author: NalaTheThird
//
// This mod aims to fix the bHaptics setup and actual runtime of the current Bhaptics.Tact library that is WILDLY outdated for our version of FrooxEngine (.NET 9) by replacing the legacy SDK (using .NET2)
// We now use a modernized version that uses proper async/await patterns instead of the broken BeginInvoke/EndInvoke methods that both .NET2 uses, and were causing massive lag spikes and frame drops for the current build of Resonite.
// And, of course, the entire function would just not work (Even in Pre-Split Branch)
// Ive spent WAY too long on this mod, so if your reading this from a decompiler or on Github/Own IDE directly, PLEASE rate this repository a star on GitHub or support me on KoFi- it helps me out a lot emotionally <3
// https://github.com/nalathethird/Resonite-bHapticsSDK2Patch
// https://ko-fi.com/nalathethird
// 
//
// Whats the big deal?:
// - Modern bHaptics SDK (async/await, WebSocketSharp, proper events)
// - Hot-plug device support (connect/disconnect ANY bHaptics devices without restarting)
 // - Battery level monitoring via DynamicVariables (for FrooxEngine - customize to your liking on your avatar!)
// - Event-driven device detection (no polling like our old system - mainly bulky and constant triggers would drop average FPS by 40% *On my machine*)
// - Automatic reconnection on bHapticsPlayer crashes or disconnects!

using Elements.Core;

using HarmonyLib;

using ResoniteModLoader;

using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	public class bHapticsManager : ResoniteMod {
		internal const string VERSION_CONSTANT = "1.0.0";
		public override string Name => "bHapticsManager";
		public override string Author => "NalaTheThird";
		public override string Version => VERSION_CONSTANT;
		public override string Link => "https://github.com/nalathethird/Resonate-bHapticsSDK2Patch";

		// Shared states (accessed by our lovely separated files - they feel, "oh so alone!")
		public static ModConfiguration Config;
		public static ModernBHaptics.bHapticsConnection Connection;

		// === USER CONFIG OPTIONS ===
	
		[AutoRegisterConfigKey]
		public static readonly ModConfigurationKey<bool> ENABLE_HOTPLUG =
			new("enable_hotplug", "Allow devices to connect/disconnect without restarting Resonite", () => true);

		[AutoRegisterConfigKey]
		public static readonly ModConfigurationKey<bool> ENABLE_SELF_HAPTICS =
			new("enable_self_haptics", "Allow your own touches to trigger your haptics (experimental)", () => false);

		// Internal constants (not exposed to config - these are optimized values - !PLEASE DO NOT TOUCH THESE UNLESS YOU KNOW EXACTLY WHAT YOU ARE DOING!)
		internal const int CONNECTION_TIMEOUT_MS = 10000; // 10 seconds is long enough to where we dont have lag spikes but also not too long to where the user is waiting forever.
		internal const int DEVICE_CHECK_CACHE_MS = 1000;
		internal const int MAX_RETRIES = 10;
		internal const bool AUTO_RECONNECT = true;

		public override void OnEngineInit() {
			Msg($"[{Name}] v{VERSION_CONSTANT}, is Initializing... {Author} was here :>");
			Config = GetConfiguration();
			Config.Save(true);
			Msg($"[{Name}] Config has loaded!");

			BHapticsConnection.Initialize();

			var harmony = new Harmony("com.nalathethird.bHapticsManager");
			harmony.PatchAll();

			Msg($"[{Name}] All Harmony patches applied");
			Msg($"[{Name}] Initialization complete");
			
			if (!Config.GetValue(ENABLE_HOTPLUG)) {
				Warn($"[{Name}] Hot-plug is DISABLED - devices must be connected before starting Resonite");
			}
			
			// Hook into Engine's shutdown callback instead of ProcessExit
			FrooxEngine.Engine.Current.OnShutdown += OnEngineShutdown;
		}

		private void OnEngineShutdown() {
			try {
				Msg($"[{Name}] Engine shutting down - stopping haptics...");
				
				// CRITICAL: Force immediate shutdown without waiting
				try {
					ModernBHaptics.bHapticsManager.StopPlayingAll();
				} catch { }
				
				try {
					ModernBHaptics.bHapticsManager.Disconnect();
				} catch { }
				
				// Give it 100ms max then move on
				System.Threading.Thread.Sleep(100);
				
				Msg($"[{Name}] Shutdown complete");
			}
			catch (Exception ex) {
				Error($"[{Name}] Error during shutdown: {ex.Message}");
			}
		}

		public static void Error(Exception ex) {
			ResoniteMod.Error(ex);
		}
	}
}
