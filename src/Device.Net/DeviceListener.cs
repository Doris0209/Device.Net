﻿

using Device.Net.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using timer = System.Timers.Timer;

namespace Device.Net
{

    public sealed class DeviceListener : IDeviceListener
    {
        #region Fields
        private bool _IsDisposed;
        private readonly timer _PollTimer;
        private readonly SemaphoreSlim _ListenSemaphoreSlim = new SemaphoreSlim(1, 1);
        private readonly ILogger _logger;

        /// <summary>
        /// This is the list of Devices by their filter definition. Note this is not actually keyed by the connected definition.
        /// </summary>
        private readonly Dictionary<string, IDevice> _CreatedDevicesByDefinition = new Dictionary<string, IDevice>();
        #endregion

        #region Public Properties
        public IDeviceManager DeviceManager { get; }
        #endregion

        #region Events
        public event EventHandler<DeviceEventArgs> DeviceInitialized;
        public event EventHandler<DeviceEventArgs> DeviceDisconnected;
        #endregion

        #region Constructor
        /// <summary>
        /// Handles connecting to and disconnecting from a set of potential devices by their definition
        /// </summary>
        public DeviceListener(
            IDeviceManager deviceManager,
            int? pollMilliseconds,
            ILoggerFactory loggerFactory)
        {
            _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<DeviceListener>();

            DeviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));

            if (!pollMilliseconds.HasValue) return;

            _PollTimer = new timer(pollMilliseconds.Value);
            _PollTimer.Elapsed += PollTimer_Elapsed;
        }
        #endregion

        #region Event Handlers
        private async void PollTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_IsDisposed)
                return;
            await CheckForDevicesAsync();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Starts the polling for devices if polling is being used.
        /// </summary>
        public void Start()
        {
            if (_PollTimer == null)
            {
                throw new ValidationException(Messages.ErrorMessagePollingNotEnabled);
            }

            _PollTimer.Start();
        }

        public async Task CheckForDevicesAsync()
        {
            try
            {
                if (_IsDisposed) return;
                await _ListenSemaphoreSlim.WaitAsync();

                var connectedDeviceDefinitions = await DeviceManager.GetConnectedDeviceDefinitionsAsync();

                //Iterate through connected devices
                foreach (var connectedDeviceDefinition in connectedDeviceDefinitions)
                {
                    //TODO: What to do if there are multiple?

                    IDevice device = null;
                    if (_CreatedDevicesByDefinition.ContainsKey(connectedDeviceDefinition.DeviceId))
                    {
                        device = _CreatedDevicesByDefinition[connectedDeviceDefinition.DeviceId];
                    }

                    if (device == null)
                    {
                        //Need to use the connected device def here instead of the filter version because the filter version won't have the id or any details
                        device = await DeviceManager.GetDevice(connectedDeviceDefinition);

                        if (device == null)
                        {
                            _logger.LogWarning("A connected device with id {deviceId} was detected but the factory didn't create an instance of it. Bad stuff is going to happen now.", connectedDeviceDefinition.DeviceId);
                        }
                        else
                        {
                            _CreatedDevicesByDefinition.Add(connectedDeviceDefinition.DeviceId, device);
                        }
                    }

                    if (device.IsInitialized) continue;

                    _logger.LogDebug("Attempting to initialize with DeviceId of {deviceId}", device.DeviceId);

                    //The device is not initialized so initialize it
                    await device.InitializeAsync();

                    //Let listeners know a registered device was initialized
                    DeviceInitialized?.Invoke(this, new DeviceEventArgs(device));

                    _logger.LogDebug(Messages.InformationMessageDeviceConnected, device.DeviceId);
                }

                var removeDeviceIds = new List<string>();

                //Iterate through registered devices
                foreach (var deviceId in _CreatedDevicesByDefinition.Keys)
                {
                    var device = _CreatedDevicesByDefinition[deviceId];

                    if (connectedDeviceDefinitions.Any(cdd => cdd.DeviceId == deviceId)) continue;

                    if (!device.IsInitialized) continue;

                    //Let listeners know a registered device was disconnected
                    //NOTE: let the rest of the app know before disposal so that the app can stop doing whatever it's doing.
                    DeviceDisconnected?.Invoke(this, new DeviceEventArgs(device));

                    //The device is no longer connected so close it
                    device.Close();

                    removeDeviceIds.Add(deviceId);

                    _logger.LogDebug(Messages.InformationMessageDeviceListenerDisconnected);
                }

                foreach (var deviceId in removeDeviceIds)
                {
                    _CreatedDevicesByDefinition.Remove(deviceId);
                }

                _logger.LogDebug(Messages.InformationMessageDeviceListenerPollingComplete);

            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                //Log and move on
                _logger.LogError(ex, Messages.ErrorMessagePollingError);

                //TODO: What else to do here?
            }
            finally
            {
                if (!_IsDisposed)
                    _ListenSemaphoreSlim.Release();
            }
        }

        public void Stop() => _PollTimer.Stop();

        public void Dispose()
        {
            if (_IsDisposed) return;
            _IsDisposed = true;

            Stop();

            _PollTimer?.Dispose();

            foreach (var key in _CreatedDevicesByDefinition.Keys)
            {
                _CreatedDevicesByDefinition[key].Dispose();
            }

            _CreatedDevicesByDefinition.Clear();

            _ListenSemaphoreSlim.Dispose();

            DeviceInitialized = null;
            DeviceDisconnected = null;

            GC.SuppressFinalize(this);
        }
        #endregion

        #region Finalizer
        ~DeviceListener()
        {
            Dispose();
        }
        #endregion
    }


}

