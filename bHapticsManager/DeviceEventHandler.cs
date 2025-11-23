using System;
using Elements.Core;
using FrooxEngine;
using ResoniteModLoader;
using HarmonyLib;
using ModernBHaptics = bHapticsLib;
using LegacyBHaptics = Bhaptics.Tact;

namespace bHapticsManager {
	
	public class DeviceEventHandler : IDisposable
    {
        private volatile bool _disposed;
        private ModernBHapticsWorkerThread _workerThread = null!;
        private readonly object _lock = new object();

        public void Initialize(ModernBHapticsWorkerThread workerThread)
        {
            _workerThread = workerThread;
            
            try
            {
                BHapticsConnection.Instance.DeviceConnected += OnDeviceConnected;
                BHapticsConnection.Instance.DeviceDisconnected += OnDeviceDisconnected;
                
                ResoniteMod.Debug("Event handlers subscribed successfully");
            }
            catch (Exception ex)
            {
                bHapticsManager.Error($"Failed to subscribe event handlers: {ex}");
            }
        }

        private void OnDeviceConnected(LegacyBHaptics.PositionType position)
        {
            if (_disposed) return;

            try
            {
                bHapticsManager.Msg($"Device {position} connected");
                
                if (Engine.Current != null && Engine.Current.WorldManager != null)
                {
                    var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
                    if (focusedWorld != null)
                    {
                        focusedWorld.RunSynchronously(() =>
                        {
                            try
                            {
                                DeviceRegistration.RegisterDevice(position);
                            }
                            catch (Exception ex)
                            {
                                bHapticsManager.Error($"Failed to register device {position}: {ex}");
                            }
                        });
                    }
                }

                lock (_lock)
                {
                    _workerThread?.OnDeviceConnected(position);
                }
            }
            catch (Exception ex)
            {
                bHapticsManager.Error($"Error handling device connection for {position}: {ex}");
            }
        }

        private void OnDeviceDisconnected(LegacyBHaptics.PositionType position)
        {
            if (_disposed) return;

            try
            {
                bHapticsManager.Msg($"Device {position} disconnected");
                
                if (Engine.Current != null && Engine.Current.WorldManager != null)
                {
                    var focusedWorld = Engine.Current.WorldManager.FocusedWorld;
                    if (focusedWorld != null)
                    {
                        focusedWorld.RunSynchronously(() =>
                        {
                            try
                            {
                                DeviceRegistration.UnregisterDevice(position);
                            }
                            catch (Exception ex)
                            {
                                bHapticsManager.Error($"Failed to unregister device {position}: {ex}");
                            }
                        });
                    }
                }

                lock (_lock)
                {
                    _workerThread?.OnDeviceDisconnected(position);
                }
            }
            catch (Exception ex)
            {
                bHapticsManager.Error($"Error handling device disconnection for {position}: {ex}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            
            try
            {
                if (BHapticsConnection.Instance != null)
                {
                    BHapticsConnection.Instance.DeviceConnected -= OnDeviceConnected;
                    BHapticsConnection.Instance.DeviceDisconnected -= OnDeviceDisconnected;
                }
                ResoniteMod.Debug("Event handlers unsubscribed");
            }
            catch (Exception ex)
            {
                bHapticsManager.Error($"Error disposing event handlers: {ex}");
            }

            lock (_lock)
            {
                _workerThread = null!;
            }
        }
    }
}
