using Elements.Core;
using FrooxEngine;
using System.Collections.Concurrent;

namespace bHapticsManager {
	internal class RemoteHapticSource : IDirectHapticSource {
		private static readonly ConcurrentDictionary<int, RemoteHapticData> _remoteData = new();
		
		private static DateTime _lastCleanup = DateTime.Now;
		private const int CLEANUP_INTERVAL_MS = 1000;
		private const int STALE_DATA_MS = 500;
		
		private readonly int _hapticPointIndex;
		private readonly World _world;
		private readonly RefID _refId;
		private readonly DummyWorldElement _dummyParent;
		
		public RefID ReferenceID => _refId;
		public string Name => $"RemoteHapticSource_{_hapticPointIndex}";
		public World World => _world;
		public IWorldElement Parent => _dummyParent;
		public bool IsLocalElement => false;
		public bool IsPersistent => false;
		public bool IsRemoved => false;
		
		private RemoteHapticSource(int hapticPointIndex, World world) {
			_hapticPointIndex = hapticPointIndex;
			_world = world;
			ulong hash = (ulong)$"RemoteHapticSource_{hapticPointIndex}_{Guid.NewGuid()}".GetHashCode();
			_refId = new RefID(hash);
			_dummyParent = new DummyWorldElement(world);
		}
		
		public float GetIntensity(SensationClass sensation) {
			if (_remoteData.TryGetValue(_hapticPointIndex, out var data)) {
				if ((DateTime.Now - data.Timestamp).TotalMilliseconds < STALE_DATA_MS) {
					return sensation switch {
						SensationClass.Force => data.Force,
						SensationClass.Temperature => data.Temperature,
						SensationClass.Pain => data.Pain,
						SensationClass.Vibration => data.Vibration,
						_ => 0f
					};
				}
			}
			return 0f;
		}
		
		public static void UpdateRemoteData(int index, float force, float temp, float pain, float vib) {
			_remoteData[index] = new RemoteHapticData {
				Force = force,
				Temperature = temp,
				Pain = pain,
				Vibration = vib,
				Timestamp = DateTime.Now
			};
		}
		
		public static bool HasRemoteData(int index) {
			if (_remoteData.TryGetValue(index, out var data)) {
				bool isRecent = (DateTime.Now - data.Timestamp).TotalMilliseconds < STALE_DATA_MS;
				bool isActive = data.Force > 0f || data.Temperature != 0f || data.Pain > 0f || data.Vibration > 0f;
				return isRecent && isActive;
			}
			return false;
		}
		
		public static void RegisterForPoint(HapticPoint point, World world) {
			var source = new RemoteHapticSource(point.Index, world);
			point.RegisterDirectSource(source);
		}
		
		public static void ClearRemoteData(int index) {
			_remoteData.TryRemove(index, out _);
		}
		
		public static void CleanupStaleData() {
			var now = DateTime.Now;
			
			if ((now - _lastCleanup).TotalMilliseconds < CLEANUP_INTERVAL_MS) {
				return;
			}
			_lastCleanup = now;
			
			var keysToRemove = new List<int>();
			foreach (var kvp in _remoteData) {
				if ((now - kvp.Value.Timestamp).TotalMilliseconds > STALE_DATA_MS) {
					keysToRemove.Add(kvp.Key);
				}
			}
			
			foreach (var key in keysToRemove) {
				_remoteData.TryRemove(key, out _);
			}
		}
		
		public void ChildChanged(IWorldElement child) { }
		public DataTreeNode Save(SaveControl control) => default!;
		public void Load(DataTreeNode node, LoadControl control) { }
		public string GetSyncMemberName(ISyncMember member) => string.Empty;
		
		private class DummyWorldElement : IWorldElement {
			private readonly World _world;
			
			public DummyWorldElement(World world) {
				_world = world;
			}
			
			public RefID ReferenceID => RefID.Null;
			public string Name => "DummyParent";
			public World World => _world;
			public IWorldElement Parent => this;
			public bool IsLocalElement => false;
			public bool IsPersistent => false;
			public bool IsRemoved => false;
			public void ChildChanged(IWorldElement child) { }
			public DataTreeNode Save(SaveControl control) => default!;
			public void Load(DataTreeNode node, LoadControl control) { }
			public string GetSyncMemberName(ISyncMember member) => string.Empty;
		}
		
		private struct RemoteHapticData {
			public float Force;
			public float Temperature;
			public float Pain;
			public float Vibration;
			public DateTime Timestamp;
		}
	}
}
