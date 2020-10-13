﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using wde = Windows.Devices.Enumeration;

namespace Device.Net.UWP
{
    public delegate Task<ConnectionInfo> TestConnection(string deviceId);

    public class UwpDeviceEnumerator
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly string aqsFilter;
        private readonly SemaphoreSlim _TestConnectionSemaphore = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, ConnectionInfo> _ConnectionTestedDeviceIds = new Dictionary<string, ConnectionInfo>();
        private readonly DeviceType _deviceType;
        private readonly TestConnection _testConnection;

        #region Constructor
        public UwpDeviceEnumerator(
            string aqf,
            DeviceType deviceType,
            TestConnection testConnection,
            ILoggerFactory loggerFactory = null)
        {
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            _logger = _loggerFactory.CreateLogger<UwpDeviceEnumerator>();
            aqsFilter = aqf;
            _deviceType = deviceType;
            _testConnection = testConnection;
        }
        #endregion

        #region Public Methods
        public async Task<IEnumerable<ConnectedDeviceDefinition>> GetConnectedDeviceDefinitionsAsync()
        {
            var deviceInformationCollection = aqsFilter != null
                ? await wde.DeviceInformation.FindAllAsync(aqsFilter).AsTask()
                : await wde.DeviceInformation.FindAllAsync().AsTask();

            var deviceInformationList = deviceInformationCollection.ToList();
            var deviceDefinitions = deviceInformationList.Select(d => DeviceBase.GetDeviceDefinitionFromWindowsDeviceId(d.Id, _deviceType, _logger));

            var deviceDefinitionList = new List<ConnectedDeviceDefinition>();

            foreach (var deviceDef in deviceDefinitions)
            {
                var connectionInformation = await TestConnection(deviceDef.DeviceId);
                if (connectionInformation.CanConnect)
                {
                    deviceDefinitionList.Add(
                        new ConnectedDeviceDefinition(
                            deviceDef.DeviceId,
                            _deviceType,
                            usagePage: connectionInformation.UsagePage,
                            vendorId: deviceDef.VendorId,
                            productId: deviceDef.ProductId
                            ));
                }
            }

            return deviceDefinitionList;
        }

        private async Task<ConnectionInfo> TestConnection(string deviceId)
        {
            using var logScope = _logger?.BeginScope("DeviceId: {deviceId} Call: {call}", deviceId, nameof(TestConnection));

            try
            {
                await _TestConnectionSemaphore.WaitAsync();

                if (_ConnectionTestedDeviceIds.TryGetValue(deviceId, out var connectionInfo)) return connectionInfo;

                connectionInfo = await _testConnection(deviceId);

                _ConnectionTestedDeviceIds.Add(deviceId, connectionInfo);

                return connectionInfo;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, Messages.ErrorMessageCouldntIntializeDevice);
                return new ConnectionInfo { CanConnect = false };
            }
            finally
            {
                _TestConnectionSemaphore.Release();
            }
        }
        #endregion
    }
}
