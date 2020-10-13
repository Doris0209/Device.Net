﻿#if !NET45

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Device.Net.UnitTests
{
    public class IntegrationTester
    {
        private readonly IDeviceFactory _deviceFactory;
        private readonly ILoggerFactory _loggerFactory;

        public IntegrationTester(
            IDeviceFactory deviceFactory,
            //TODO: Mock this
            ILoggerFactory loggerFactory = null
            )
        {
            _deviceFactory = deviceFactory ?? throw new ArgumentNullException(nameof(deviceFactory));
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        }

        public async Task TestAsync(byte[] writeData, Func<ReadResult, IDevice, Task> assertFunc, int expectedDataLength)
        {
            var deviceManager = _deviceFactory.ToDeviceManager(_loggerFactory);

            var devices = await deviceManager.GetConnectedDeviceDefinitionsAsync();

            //Get the first available device
            var deviceDefinition = devices.FirstOrDefault();

            //Ensure that it gets picked up
            Assert.IsNotNull(deviceDefinition);

            var device = await deviceManager.GetDevice(deviceDefinition);

            //Initialize the device
            await device.InitializeAsync();

            var result = await device.WriteAndReadAsync(writeData);

            Assert.AreEqual((uint)expectedDataLength, result.BytesRead);
            Assert.AreEqual(expectedDataLength, result.Data.Length);

            await assertFunc(result, device);
        }
    }
}

#endif
