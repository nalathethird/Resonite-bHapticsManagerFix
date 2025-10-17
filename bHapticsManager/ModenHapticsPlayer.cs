using System;
using System.Collections.Generic;

using Bhaptics.Tact;

using Elements.Core;

using FrooxEngine;

using SkyFrost.Base;

/// <summary>
/// Modern wrapper for bHaptics device control compatible with FrooxEngine threading.
/// </summary>
public class ModernHapticsPlayer {
	private HapticPlayer hapticPlayer;
	private IPlatformProfile platform;
	private bool initialized;

	public bool IsInitialized => initialized && hapticPlayer != null;

	public ModernHapticsPlayer(IPlatformProfile platform) {
		this.platform = platform;
	}

	public bool Initialize() {
		try {
			hapticPlayer = new HapticPlayer(platform.Abbreviation, platform.Name);
			initialized = true;
			UniLog.Log("[ModernHapticsPlayer] Initialized successfully.");
			return true;
		} catch (Exception ex) {
			UniLog.Error("[ModernHapticsPlayer] Failed to initialize: " + ex);
			initialized = false;
			return false;
		}
	}

	public bool IsDeviceActive(Bhaptics.Tact.PositionType type) {
		if (!IsInitialized)
			return false;

		try {
			return hapticPlayer.IsActive(type);
		} catch {
			return false;
		}
	}

	public void Submit(string key, Bhaptics.Tact.PositionType position, List<DotPoint> points, int durationMillis) {
		if (!IsInitialized) return;
		try {
			hapticPlayer.Submit(key, position, points, durationMillis);
		} catch (Exception ex) {
			UniLog.Error($"[ModernHapticsPlayer] Submit error ({position}): {ex.Message}");
		}
	}

	public void Dispose() {
		if (hapticPlayer != null) {
			try {
				hapticPlayer.Dispose();
				UniLog.Log("[ModernHapticsPlayer] Disposed safely.");
			} catch (Exception ex) {
				UniLog.Error("[ModernHapticsPlayer] Dispose error: " + ex.Message);
			}
		}
		initialized = false;
	}
}
