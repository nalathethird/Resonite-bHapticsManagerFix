// LegacyCompatibilityLayer.cs
// Provides exact Bhaptics.Tact behavior using modern bHapticsLib

using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	/// <summary>
	/// Replicates the exact submission behavior of legacy Bhaptics.Tact.HapticPlayer
	/// to ensure timing and patterns work identically.
	/// </summary>
	public static class LegacyCompatibilityLayer {
		
		// Track last submission time per DEVICE+KEY combination
		private static readonly Dictionary<string, DateTime> _lastSubmissionTime = new();
		
		// Track when each device last had non-zero data (for idle detection)
		private static readonly Dictionary<LegacyBHaptics.PositionType, DateTime> _lastActiveTime = new();
		
		private static readonly object _submissionLock = new object();
		private const int MIN_SUBMISSION_INTERVAL_MS = 35; // 28 Hz max
		private const int IDLE_TIMEOUT_MS = 100; // Stop sending after 100ms of zeros
		
		/// <summary>
		/// Submits haptics using the EXACT same logic as legacy HapticPlayer.Submit()
		/// </summary>
		public static void SubmitFrame(string key, LegacyBHaptics.PositionType position, 
			List<LegacyBHaptics.DotPoint> dotPoints, int durationMillis) {
			
			// Check if device has non-zero data
			bool hasActiveMotors = dotPoints != null && dotPoints.Any(p => p.Intensity > 0);
			
			lock (_submissionLock) {
				// Update last active time if there's data
				if (hasActiveMotors) {
					_lastActiveTime[position] = DateTime.Now;
				}
				
				// IDLE DETECTION: If device has been zero for more than 100ms, stop sending
				if (!hasActiveMotors) {
					if (_lastActiveTime.TryGetValue(position, out DateTime lastActive)) {
						double idleTimeMs = (DateTime.Now - lastActive).TotalMilliseconds;
						if (idleTimeMs > IDLE_TIMEOUT_MS) {
							// Device is idle - don't spam with zero data
							return;
						}
					} else {
						// Never had data, skip
						return;
					}
				}
				
				// Rate limit per DEVICE+KEY
				string deviceKey = $"{position}_{key}";
				
				if (_lastSubmissionTime.TryGetValue(deviceKey, out DateTime lastTime)) {
					double timeSinceLastMs = (DateTime.Now - lastTime).TotalMilliseconds;
					if (timeSinceLastMs < MIN_SUBMISSION_INTERVAL_MS) {
						return;
					}
				}
				_lastSubmissionTime[deviceKey] = DateTime.Now;
			}
			
			var modernPosition = PositionMapper.MapLegacyToModern(position);
			
			// Convert DotPoints to modern format (filter zeros)
			var modernDotPoints = new List<ModernBHaptics.DotPoint>();
			if (dotPoints != null) {
				foreach (var legacy in dotPoints) {
					if (legacy.Intensity > 0) {
						modernDotPoints.Add(new ModernBHaptics.DotPoint(legacy.Index, legacy.Intensity));
					}
				}
			}
			
			// Skip if no active motors
			if (modernDotPoints.Count == 0) {
				return;
			}
			
			// Extend duration slightly to prevent gaps
			int extendedDuration = Math.Max(80, durationMillis * 2);
			
			// Create empty PathPoints list
			var emptyPathPoints = new List<ModernBHaptics.PathPoint>();
			
			// Use bHapticsConnection if available
			var conn = bHapticsManager.Connection;
			bool useManager = conn == null || conn.Status != ModernBHaptics.bHapticsStatus.Connected;
			
			try {
				if (useManager) {
					// Check if device is connected
					if (!ModernBHaptics.bHapticsManager.IsDeviceConnected(modernPosition)) {
						// Device disconnected - clean up tracking
						CleanupDevice(position);
						return;
					}
					
					// Submit pattern
					ModernBHaptics.bHapticsManager.Play(
						key, 
						extendedDuration, 
						modernPosition, 
						modernDotPoints, 
						emptyPathPoints
					);
				}
				else {
					// Use connection Play() method
					conn.Play(
						key,
						extendedDuration,
						modernPosition,
						modernDotPoints,
						emptyPathPoints,
						ModernBHaptics.MirrorDirection.None
					);
				}
			}
			catch {
				// Silent fail
			}
		}
		
		/// <summary>
		/// Legacy SDK converts byte[] to DotPoints, filtering out zeros
		/// </summary>
		public static void SubmitFrame(string key, LegacyBHaptics.PositionType position, 
			byte[] motorBytes, int durationMillis) {
			
			// Replicate exact legacy behavior: convert byte[] to DotPoint list
			List<LegacyBHaptics.DotPoint> dotPoints = new List<LegacyBHaptics.DotPoint>();
			
			for (int i = 0; i < motorBytes.Length; i++) {
				if (motorBytes[i] > 0) {
					dotPoints.Add(new LegacyBHaptics.DotPoint(i, motorBytes[i]));
				}
			}
			
			SubmitFrame(key, position, dotPoints, durationMillis);
		}
		
		/// <summary>
		/// Clean up tracking data for a specific device
		/// </summary>
		public static void CleanupDevice(LegacyBHaptics.PositionType position) {
			lock (_submissionLock) {
				_lastActiveTime.Remove(position);
				
				// Remove all submission times for this device
				var keysToRemove = _lastSubmissionTime
					.Where(kvp => kvp.Key.StartsWith($"{position}_"))
					.Select(kvp => kvp.Key)
					.ToList();
				
				foreach (var key in keysToRemove) {
					_lastSubmissionTime.Remove(key);
				}
			}
		}
		
		/// <summary>
		/// Reset device state when it reconnects - clears idle status so it can receive data immediately
		/// </summary>
		public static void ResetDevice(LegacyBHaptics.PositionType position) {
			lock (_submissionLock) {
				// Clear idle state by removing last active time
				_lastActiveTime.Remove(position);
				
				// Clear rate limiting history so first submission goes through immediately
				var keysToRemove = _lastSubmissionTime
					.Where(kvp => kvp.Key.StartsWith($"{position}_"))
					.Select(kvp => kvp.Key)
					.ToList();
				
				foreach (var key in keysToRemove) {
					_lastSubmissionTime.Remove(key);
				}
			}
		}
		
		/// <summary>
		/// Clean up old submission timestamps to prevent memory leak
		/// </summary>
		public static void CleanupOldSubmissions() {
			lock (_submissionLock) {
				var now = DateTime.Now;
				
				// Remove old submission times
				var keysToRemove = _lastSubmissionTime
					.Where(kvp => (now - kvp.Value).TotalSeconds > 5)
					.Select(kvp => kvp.Key)
					.ToList();
				
				foreach (var key in keysToRemove) {
					_lastSubmissionTime.Remove(key);
				}
				
				// Remove old active times
				var devicesToRemove = _lastActiveTime
					.Where(kvp => (now - kvp.Value).TotalSeconds > 10)
					.Select(kvp => kvp.Key)
					.ToList();
				
				foreach (var device in devicesToRemove) {
					_lastActiveTime.Remove(device);
				}
			}
		}
	}
}
