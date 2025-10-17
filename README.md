# Resonite bHapticsManager (Mod)

A [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod for [Resonite](https://resonite.com/) that modernizes bHaptics integration with the new SDK2 (bHapticsLib v1.0.8+).

## What does this mod do?

This mod replaces Resonite's outdated bHaptics SDK1 (Bhaptics.Tact) with a modern version of **bHapticsLib v1.0.8** (SDK2+), providing:

- **Hot-plug support** - Connect/disconnect bHaptics devices without restarting Resonite
- **No frame drops** - Eliminates lag spikes caused by the legacy SDK's broken async patterns from current Resonite version (Beta 2025.9.23.1237)
- **Proper async/await** - Modern WebSocket implementation instead of deprecated BeginInvoke/EndInvoke from old .NET2 method (that also relied on a seperate assembly)
- **Event-driven device detection** - No more constant polling (which furthered the Frame Drop issue above)
- **Automatic reconnection** - Seamlessly reconnects if bHaptics Player crashes or devices are disconnected and reconnected

## Requirements

**IMPORTANT**: You must have **bHapticsLib.dll v1.0.8 or above** installed before using this mod!

### Installation Steps

1. Install [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader)
   - If you use [Resolute](https://github.com/Gawdl3y/Resolute/releases/latest), this process is streamlined for you and you can skip to Step 2
2. Download [bHapticsManager.dll](https://github.com/nalathethird/Resonite-bHapticsSDK2Patch/releases/latest/download/bHapticsManager.dll)
3. Place `bHapticsManager.dll` in your `rml_mods` folder
   - Path: `C:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_mods\`
4. Download [bhapticsLib.dll](https://github.com/nalathethird/bHapticsLib/releases/latest/download/bHapticsLib.dll)
5. Place `bhapticsLib.dll` in your `rml_libs` folder
   - Path `C:\Program Files (x86)\Steam\steamapps\common\Resonite\rml_lib\`
   - (Create the folder in your Resonite Directory if it doesn't exist)
6. Start Resonite and check your logs to verify the mod loaded successfully

## Known Issues

- **Buffer jam**: Occasionally the haptic event buffer may become congested (this might be caused by the Event-Driven DeviceDetection instead of constant polling), causing a 1-2 second delay in haptic response. This resolves automatically - just wait a moment and haptics will catch up.

## Differences from Legacy bHaptics (Native FrooxEngine)

### What's Fixed:
- The main method of error was `BeginInvoke/EndInvoke` calls on a legacy .NET that was not existant in FrooxEngine's Code (due to splitting mostly off Unity's Mono and Runtimes)
- bHapticsManager Mod alows devices to connect/disconnect at will! and (should) add proper shutdown and startup haptics devices if you want to add more while in your session! Yay!
- Haptic Points in Resonite now resolve and send Haptic Feedback to the Client and Player!

### What's New:
- Modern async/await patterns
- WebSocket-based communication from a custom Lib *see [bHapticsLib](https://github.com/nalathethird/bHapticsLib)* - Thanks to HerpDerpinstine for the main framework of this Lib!
- Rate limiting to prevent buffer overflow
- Idle detection (stops sending when motors are at zero)
- Clean device lifecycle management

## Configuration

The mod has minimal configuration options (by design):

- **`enable_hotplug`** (default: `true`) - Allow devices to connect/disconnect without restarting
- **`enable_self_haptics`** (default: `false`) - Enable your own touches to trigger your haptics (experimental)

To change settings, edit the mod config with [ResoniteModSettings](https://github.com/badhaloninja/ResoniteModSettings/releases/) or the config file created on first launch.

## Troubleshooting

### Mod won't load
- Ensure `bHapticsLib.dll v1.0.8` or above is in `rml_libs` folder
- Check Resonite logs for specific error messages

### Devices not detected
- Make sure bHaptics Player is running
- Check that devices are connected in bHaptics Player first
- If `enable_hotplug` is disabled (setting is enabled by default), devices must be on before starting Resonite

### Haptics feel weak/pulsing
- This is normal during initial connection (buffer stabilization)
- Should smooth out after 5-10 seconds
- If persistent, try reconnecting any devices. Otherwise, create an [issue](https://github.com/nalathethird/Resonite-bHapticsManagerFix/issues/new/choose)!

### Self-touch not working
- Enable `enable_self_haptics` in mod config
- Requires `DirectTagHapticSource` components on your avatar
- Works with TipTouchSource (finger/tool touches)

## Support

If you encounter issues:
1. Check [GitHub Issues](https://github.com/nalathethird/Resonite-bHapticsSDK2Patch/issues)
2. Provide your Resonite log file
3. Describe your bHaptics device setup
---

**Star this repo if it helped you!** ⭐ It keeps me motivated to maintain and improve the mod.

Or, if you want to go further, Support me on [Ko-fi!](https://ko-fi.com/nalathethird) ☕
It helps me pay bills, and other things someone whos unemployed cant pay!
****
## Credits

- **[bHapticsLib](https://github.com/HerpDerpinstine/bHapticsLib)**: HerpDerpinstine
- **Testing & Feedback**: Resonite community
