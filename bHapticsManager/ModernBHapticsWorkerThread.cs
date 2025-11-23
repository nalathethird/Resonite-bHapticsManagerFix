using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Elements.Core;
using FrooxEngine;
using ResoniteModLoader;
using LegacyBHaptics = Bhaptics.Tact;
using ModernBHaptics = bHapticsLib;

namespace bHapticsManager {
	public class ModernBHapticsWorkerThread : IDisposable {
		private class HapticPointData(HapticPoint point) {
			public HapticPoint Point { get; } = point;
			public float TempPhi { get; set; }
			public float VibrationPhi { get; set; }
		}
		
		private const int UPDATE_INTERVAL_MS = 8;
		private const int SUBMISSION_DURATION_MS = 100;
		
		private readonly InputInterface _inputInterface;
		private readonly CancellationTokenSource _cancellationTokenSource = new();
		private readonly Thread _workerThread;
		private readonly Dictionary<LegacyBHaptics.PositionType, List<HapticPointData>> _hapticPointsByDevice = [];
		private readonly Dictionary<LegacyBHaptics.PositionType, string> _deviceKeys = [];
		
		private float _globalPainPhi = 0f;
		private int _frameCount = 0;
		private DateTime _lastStatsReport = DateTime.Now;
		private volatile bool _running;
		private volatile bool _disposed;
		private readonly object _lock = new();
		private readonly List<HapticPoint> _points = new();
		private readonly Dictionary<string, DeviceState> _deviceStates = new();
		private readonly AutoResetEvent _workEvent = new(false);

		private class DeviceState {
			public string DeviceId { get; set; } = null!;
			public LegacyBHaptics.PositionType Position { get; set; }
			public bool IsConnected { get; set; }
			public readonly object Lock = new();
		}

		public ModernBHapticsWorkerThread(InputInterface inputInterface) {
			_inputInterface = inputInterface;

			_workerThread = new Thread(WorkerThreadLoop) {
				Priority = ThreadPriority.Highest,
				IsBackground = true,
				Name = "ModernBHapticsWorker"
			};
		}

		public ModernBHapticsWorkerThread(List<HapticPoint> points) {
			_inputInterface = Engine.Current.InputInterface;
			lock (_lock) {
				_points.AddRange(points);
				bHapticsManager.Msg($"Starting worker thread with {points.Count} points across {GetDeviceCount()} devices");
			}

			_running = true;
			_workerThread = new Thread(WorkerThreadLoop) {
				Priority = ThreadPriority.Highest,
				IsBackground = true,
				Name = "ModernBHapticsWorker"
			};
			_workerThread.Start();
			ResoniteMod.Debug("Worker thread started successfully");
		}

		private int GetDeviceCount() {
			var positions = new HashSet<LegacyBHaptics.PositionType>();
			foreach (var point in _points) {
				positions.Add(GetDeviceTypeFromPosition(point.Position));
			}
			return positions.Count;
		}
		
		public void Start() {
			if (_workerThread.IsAlive) {
				ResoniteMod.Warn("Worker thread already running!");
				return;
			}
			
			if (!PopulateHapticPoints()) {
				ResoniteMod.Error("Failed to populate haptic points - worker thread not started");
				return;
			}
			
			lock (_lock) {
				_running = true;
				ResoniteMod.Debug($"Starting worker thread with {GetTotalPointCount()} points across {_hapticPointsByDevice.Count} devices");
				_workerThread.Start();
			}
		}
		
		public void Stop() {
			if (_disposed) return;
			
			ResoniteMod.Debug("Stopping worker thread...");
			_running = false;
			
			try {
				_cancellationTokenSource.Cancel();
			}
			catch (ObjectDisposedException ex) {
				ResoniteMod.Warn($"Cancellation token source already disposed: {ex}");
			}
			
			_workEvent.Set();
			
			if (_workerThread != null && _workerThread.IsAlive && !_workerThread.Join(TimeSpan.FromSeconds(2))) {
				bHapticsManager.Warn("Worker thread did not stop gracefully, interrupting...");
				try {
					_workerThread.Interrupt();
					if (!_workerThread.Join(TimeSpan.FromSeconds(1))) {
						bHapticsManager.Warn("Worker thread did not respond to interrupt");
					}
				}
				catch (Exception ex) {
					bHapticsManager.Error($"Error interrupting worker thread: {ex}");
				}
			}
			
			ResoniteMod.Debug("Worker thread stopped");
		}
		
		private bool PopulateHapticPoints() {
			try {
				int totalPoints = _inputInterface.HapticPointCount;
				if (totalPoints == 0) {
					ResoniteMod.Warn("No haptic points registered in InputInterface");
					return false;
				}

				for (int i = 0; i < totalPoints; i++) {
					HapticPoint point = _inputInterface.GetHapticPoint(i);
					if (point == null) continue;

					LegacyBHaptics.PositionType deviceType = GetDeviceTypeFromPosition(point.Position);

					if (!_hapticPointsByDevice.TryGetValue(deviceType, out List<HapticPointData>? value)) {
						value = new List<HapticPointData>();
						_hapticPointsByDevice[deviceType] = value;
						_deviceKeys[deviceType] = Guid.NewGuid().ToString();
					}
					value.Add(new(point));
				}

				return true;
			}
			catch (Exception ex) {
				ResoniteMod.Error($"Error populating haptic points: {ex.Message}");
				return false;
			}
		}
		
		private LegacyBHaptics.PositionType GetDeviceTypeFromPosition(HapticPointPosition position) {
			string typeName = position.GetType().Name;
			
			return typeName switch {
				"HeadHapticPointPosition" => LegacyBHaptics.PositionType.Head,
				"TorsoHapticPointPosition" => LegacyBHaptics.PositionType.Vest,
				"ArmHapticPosition" => GetArmSide(position),
				"HandHapticPosition" => GetHandSide(position),
				"LegHapticPosition" => GetLegSide(position),
				_ => LegacyBHaptics.PositionType.Vest
			};
		}
		
		private static LegacyBHaptics.PositionType GetArmSide(HapticPointPosition position) {
			try {
				var sideProperty = position.GetType().GetProperty("Side");
				if (sideProperty != null) {
					var side = sideProperty.GetValue(position);
					if (side != null && side.ToString() == "Left") {
						return LegacyBHaptics.PositionType.ForearmL;
					}
				}
			}
			catch { }
			return LegacyBHaptics.PositionType.ForearmR;
		}

		private static LegacyBHaptics.PositionType GetHandSide(HapticPointPosition position) {
			try {
				var sideProperty = position.GetType().GetProperty("Side");
				if (sideProperty != null) {
					var side = sideProperty.GetValue(position);
					if (side != null && side.ToString() == "Left") {
						return LegacyBHaptics.PositionType.HandL;
					}
				}
			}
			catch { }
			return LegacyBHaptics.PositionType.HandR;
		}

		private static LegacyBHaptics.PositionType GetLegSide(HapticPointPosition position) {
			try {
				var sideProperty = position.GetType().GetProperty("Side");
				if (sideProperty != null) {
					var side = sideProperty.GetValue(position);
					if (side != null && side.ToString() == "Left") {
						return LegacyBHaptics.PositionType.FootL;
					}
				}
			}
			catch { }
			return LegacyBHaptics.PositionType.FootR;
		}
		
		private int GetTotalPointCount() {
			int count = 0;
			foreach (var group in _hapticPointsByDevice.Values) {
				count += group.Count;
			}
			return count;
		}
		
		private void WorkerThreadLoop() {
			List<LegacyBHaptics.DotPoint> dotPoints = [];
			
			try {
				var stopwatch = System.Diagnostics.Stopwatch.StartNew();
				long nextFrameTime = 0;
				
				while (!_cancellationTokenSource.Token.IsCancellationRequested && !_disposed && _running) {
					long currentTime = stopwatch.ElapsedMilliseconds;
					
					if (currentTime < nextFrameTime) {
						int sleepTime = (int)(nextFrameTime - currentTime);
						if (sleepTime > 0) {
							Thread.Sleep(sleepTime);
						}
					}
					
					nextFrameTime = stopwatch.ElapsedMilliseconds + UPDATE_INTERVAL_MS;
					
					float dt = UPDATE_INTERVAL_MS / 1000f;
					float maxPain = 0f;
					
					foreach (var deviceGroup in _hapticPointsByDevice.Values) {
						foreach (var pointData in deviceGroup) {
							pointData.Point.SampleSources();
							maxPain = MathX.Max(pointData.Point.Pain, maxPain);
						}
					}
					
					_globalPainPhi += MathF.PI * 2f * dt * MathX.Lerp(1.3333334f, 2.3333333f, maxPain);
					_globalPainPhi %= MathF.PI * 4f;
					
					foreach (var kvp in _hapticPointsByDevice) {
						LegacyBHaptics.PositionType deviceType = kvp.Key;
						List<HapticPointData> points = kvp.Value;
						string deviceKey = _deviceKeys[deviceType];
						
						dotPoints.Clear();
						int motorIndex = 0;
						
						foreach (var pointData in points) {
							HapticPoint point = pointData.Point;
							float intensity = point.Force;

							float painAmplitude = MathX.Pow(MathX.Abs(MathX.Sin(_globalPainPhi)), 2f) 
								* (float)MathX.Max(0, MathX.Sign(MathX.Sin(_globalPainPhi * 0.5f)));
							painAmplitude *= MathX.Pow(point.Pain, 0.5f);
							painAmplitude += RandomX.Value * MathX.Pow(point.Pain, 0.25f) * 0.1f;
							intensity = MathX.Max(intensity, painAmplitude);

							float normalizedTemp = MathX.Abs(point.Temperature / 100f);
							pointData.TempPhi += normalizedTemp * 4f;
							pointData.TempPhi %= 20000f;
							float tempAmplitude = normalizedTemp * MathX.SimplexNoise(pointData.TempPhi);
							intensity = MathX.Max(intensity, tempAmplitude);

							pointData.VibrationPhi += MathF.PI * 2f * dt * MathX.Lerp(0.1f, 10f, point.Vibration);
							pointData.VibrationPhi %= MathF.PI * 2f;
							float vibrationAmplitude = (MathX.Sin(pointData.VibrationPhi) * 0.5f + 0.5f) * point.Vibration;
							intensity = MathX.Max(intensity, vibrationAmplitude);

							int intensityInt = MathX.Clamp(MathX.RoundToInt(intensity * 100f), 0, 100);
							dotPoints.Add(new(motorIndex++, intensityInt));
						}
						
						if (dotPoints.Count > 0) {
							LegacyCompatibilityLayer.SubmitFrame(deviceKey, deviceType, dotPoints, SUBMISSION_DURATION_MS);
						}
					}
					
					_frameCount++;
					if ((DateTime.Now - _lastStatsReport).TotalSeconds >= 10) {
						if (bHapticsManager.Config?.GetValue(bHapticsManager.ENABLE_DIAGNOSTIC_LOGGING) ?? false) {
							double avgFps = _frameCount / (DateTime.Now - _lastStatsReport).TotalSeconds;
							ResoniteMod.Debug($"Worker thread: {avgFps:F1} Hz avg, {_hapticPointsByDevice.Count} devices, {GetTotalPointCount()} points");
						}
						_frameCount = 0;
						_lastStatsReport = DateTime.Now;
					}
				}
				
				ResoniteMod.Debug("Worker thread exiting normally");
			}
			catch (ThreadInterruptedException) {
				ResoniteMod.Debug("Worker thread interrupted");
			}
			catch (ThreadAbortException) {
				ResoniteMod.Debug("Worker thread aborted");
			}
			catch (Exception ex) {
				bHapticsManager.Error($"Worker thread error: {ex.Message}");
			}
			finally {
				try {
					ModernBHaptics.bHapticsManager.StopPlayingAll();
				}
				catch (Exception ex) {
					bHapticsManager.Error($"Error stopping haptic playback: {ex}");
				}
				
				lock (_lock) {
					_running = false;
				}
				ResoniteMod.Debug("Worker thread cleanup complete");
			}
		}
		
		public void OnDeviceConnected(LegacyBHaptics.PositionType position) {
			var deviceId = GetDeviceId(position);
			lock (_deviceStates) {
				if (_deviceStates.TryGetValue(deviceId, out var state)) {
					lock (state.Lock) {
						state.IsConnected = true;
					}
				} else {
					_deviceStates[deviceId] = new DeviceState {
						DeviceId = deviceId,
						Position = position,
						IsConnected = true
					};
				}
			}
			ResoniteMod.Debug($"Device {deviceId} marked as connected in worker thread");
		}

		public void OnDeviceDisconnected(LegacyBHaptics.PositionType position) {
			var deviceId = GetDeviceId(position);
			lock (_deviceStates) {
				if (_deviceStates.TryGetValue(deviceId, out var state)) {
					lock (state.Lock) {
						state.IsConnected = false;
					}
				}
			}
			ResoniteMod.Debug($"Device {deviceId} marked as disconnected in worker thread");
		}

		private string GetDeviceId(LegacyBHaptics.PositionType position) {
			return position switch {
				LegacyBHaptics.PositionType.Head => "Head",
				LegacyBHaptics.PositionType.VestFront => "VestFront",
				LegacyBHaptics.PositionType.VestBack => "VestBack",
				LegacyBHaptics.PositionType.Vest => "VestFront",
				LegacyBHaptics.PositionType.ForearmL => "ArmLeft",
				LegacyBHaptics.PositionType.ForearmR => "ArmRight",
				LegacyBHaptics.PositionType.FootL => "FootLeft",
				LegacyBHaptics.PositionType.FootR => "FootRight",
				LegacyBHaptics.PositionType.HandL => "GloveLeft",
				LegacyBHaptics.PositionType.HandR => "GloveRight",
				_ => "Unknown"
			};
		}

		public void TriggerUpdate() {
			if (!_disposed && _running) {
				_workEvent.Set();
			}
		}

		public void Dispose() {
			if (_disposed) return;
			
			ResoniteMod.Debug("Disposing worker thread...");
			_disposed = true;
			
			Stop();
			
			try {
				_cancellationTokenSource.Dispose();
			}
			catch (Exception ex) {
				bHapticsManager.Error($"Error disposing cancellation token: {ex}");
			}
			
			try {
				_workEvent.Dispose();
			}
			catch (Exception ex) {
				bHapticsManager.Error($"Error disposing work event: {ex}");
			}
			
			lock (_lock) {
				_points.Clear();
				_hapticPointsByDevice.Clear();
				_deviceKeys.Clear();
			}
			
			lock (_deviceStates) {
				_deviceStates.Clear();
			}

			ResoniteMod.Debug("Worker thread disposed");
		}
	}
}
