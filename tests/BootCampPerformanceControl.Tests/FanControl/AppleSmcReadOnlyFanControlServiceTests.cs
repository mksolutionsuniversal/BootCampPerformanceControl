using System.ComponentModel;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.FanControl;

public sealed class AppleSmcReadOnlyFanControlServiceTests
{
    [Fact]
    public async Task ReadStatusAsync_UnsupportedModel_DoesNotOpenServiceOrDevice()
    {
        var serviceOpenCount = 0;
        var transportFactory = new FakeAppleSmcTransportFactory(new FakeSmcTransport());
        var service = CreateService(
            () =>
            {
                serviceOpenCount++;
                throw new InvalidOperationException("Service controller must not be opened.");
            },
            transportFactory);

        var status = await service.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro14_3,
            CancellationToken.None);

        Assert.Equal(FanBackendState.NotApplicable, status.BackendState);
        Assert.Equal(FanSafetyState.UnsupportedModel, status.SafetyState);
        Assert.False(status.IsAvailable);
        Assert.Contains("not verified", status.Details, StringComparison.Ordinal);
        Assert.Equal(0, serviceOpenCount);
        Assert.Equal(0, transportFactory.OpenCount);
    }

    [Fact]
    public async Task ReadStatusAsync_PreCanceled_PropagatesWithoutOpeningServiceOrDevice()
    {
        var serviceOpenCount = 0;
        var transportFactory = new FakeAppleSmcTransportFactory(new FakeSmcTransport());
        var service = CreateService(
            () =>
            {
                serviceOpenCount++;
                throw new InvalidOperationException("Service controller must not be opened.");
            },
            transportFactory);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ReadStatusAsync(
                VerifiedHardwareModels.MacBookPro16_1,
                cancellationSource.Token));

        Assert.Equal(0, serviceOpenCount);
        Assert.Equal(0, transportFactory.OpenCount);
    }

    [Fact]
    public async Task ReadStatusAsync_MissingService_ReturnsNotInstalledWithoutOpeningDevice()
    {
        var transportFactory = new FakeAppleSmcTransportFactory(new FakeSmcTransport());
        var service = CreateService(
            () => throw new Win32Exception(1060, "Service does not exist."),
            transportFactory);

        var status = await service.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.Equal(FanBackendState.NotInstalled, status.BackendState);
        Assert.Equal(FanSafetyState.MonitoringUnavailable, status.SafetyState);
        Assert.Equal(0, transportFactory.OpenCount);
    }

    [Fact]
    public async Task ReadStatusAsync_StoppedService_DoesNotStartServiceOrOpenDevice()
    {
        var controller = new FakeAppleSmcServiceController(AppleSmcServiceState.Stopped);
        var transportFactory = new FakeAppleSmcTransportFactory(new FakeSmcTransport());
        var service = CreateService(() => controller, transportFactory);

        var status = await service.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.Equal(FanBackendState.InstalledStopped, status.BackendState);
        Assert.Equal(0, controller.StartCount);
        Assert.Equal(0, controller.StopCount);
        Assert.Equal(0, transportFactory.OpenCount);
        Assert.Equal(1, controller.DisposeCount);
    }

    [Theory]
    [InlineData((uint)AppleSmcServiceState.StartPending)]
    [InlineData((uint)AppleSmcServiceState.StopPending)]
    public async Task ReadStatusAsync_TransitionalService_DoesNotMutateServiceOrOpenDevice(
        uint rawState)
    {
        var state = (AppleSmcServiceState)rawState;
        var controller = new FakeAppleSmcServiceController(state);
        var transportFactory = new FakeAppleSmcTransportFactory(new FakeSmcTransport());
        var service = CreateService(() => controller, transportFactory);

        var status = await service.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.Equal(FanBackendState.Transitional, status.BackendState);
        Assert.Contains(state.ToString(), status.Details, StringComparison.Ordinal);
        Assert.Equal(0, controller.StartCount);
        Assert.Equal(0, controller.StopCount);
        Assert.Equal(0, transportFactory.OpenCount);
    }

    [Fact]
    public async Task ReadStatusAsync_ServiceStopsAfterDiscovery_DoesNotRestartOrOpenDevice()
    {
        var controller = new FakeAppleSmcServiceController(
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Stopped);
        var transportFactory = new FakeAppleSmcTransportFactory(new FakeSmcTransport());
        var service = CreateService(() => controller, transportFactory);

        var status = await service.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.Equal(FanBackendState.InstalledStopped, status.BackendState);
        Assert.Equal(0, controller.StartCount);
        Assert.Equal(0, transportFactory.OpenCount);
        Assert.Equal(1, controller.DisposeCount);
    }

    [Fact]
    public async Task ReadStatusAsync_RunningService_ReturnsStructuredValuesAndDisposesSession()
    {
        var controller = new FakeAppleSmcServiceController(
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Running);
        var transport = new FakeSmcTransport();
        var transportFactory = new FakeAppleSmcTransportFactory(transport);
        var service = CreateService(() => controller, transportFactory);

        var status = await service.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.Equal(FanBackendState.Running, status.BackendState);
        Assert.Equal(FanSafetyState.ReadOnlyVerified, status.SafetyState);
        Assert.True(status.IsAvailable);
        Assert.Equal(new FanReading(1840f, 5616f, FanOperatingMode.AppleAuto), status.Fan0);
        Assert.Equal(new FanReading(1691f, 5200f, FanOperatingMode.AppleAuto), status.Fan1);
        Assert.False(status.IsWriteControlEnabled);
        Assert.Equal(0, controller.StartCount);
        Assert.Equal(0, controller.StopCount);
        Assert.Equal(1, transportFactory.OpenCount);
        Assert.Equal(1, transport.ProtocolCalls);
        Assert.Equal(9, transport.KeyInfoCalls);
        Assert.Equal(9, transport.ReadCalls);
        Assert.True(transport.IsDisposed);
        Assert.Equal(1, controller.DisposeCount);
    }

    [Fact]
    public async Task ReadStatusAsync_SharingViolation_ReturnsBusyWithoutRetry()
    {
        var controller = new FakeAppleSmcServiceController(
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Running);
        var transportFactory = new FakeAppleSmcTransportFactory(
            new Win32Exception(32, "Sharing violation."));
        var service = CreateService(() => controller, transportFactory);

        var status = await service.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.Equal(FanBackendState.Busy, status.BackendState);
        Assert.Equal(FanSafetyState.MonitoringUnavailable, status.SafetyState);
        Assert.Equal(1, transportFactory.OpenCount);
        Assert.Equal(0, controller.StartCount);
        Assert.Equal(1, controller.DisposeCount);
    }

    [Fact]
    public async Task ReadStatusAsync_DeviceAccessDenied_ReturnsAccessDenied()
    {
        var controller = new FakeAppleSmcServiceController(
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Running);
        var transportFactory = new FakeAppleSmcTransportFactory(
            new Win32Exception(5, "Access denied."));
        var service = CreateService(() => controller, transportFactory);

        var status = await service.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.Equal(FanBackendState.AccessDenied, status.BackendState);
        Assert.Equal(FanSafetyState.MonitoringUnavailable, status.SafetyState);
        Assert.Equal(1, transportFactory.OpenCount);
        Assert.Equal(0, controller.StartCount);
        Assert.Equal(1, controller.DisposeCount);
    }

    private static AppleSmcReadOnlyFanControlService CreateService(
        Func<IAppleSmcServiceController> openServiceController,
        IAppleSmcTransportFactory transportFactory)
    {
        return new AppleSmcReadOnlyFanControlService(
            new FanSafetyPolicy(),
            openServiceController,
            transportFactory);
    }

    private sealed class FakeAppleSmcServiceController : IAppleSmcServiceController
    {
        private readonly Queue<AppleSmcServiceState> _states;

        public FakeAppleSmcServiceController(params AppleSmcServiceState[] states)
        {
            _states = new Queue<AppleSmcServiceState>(states);
        }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int DisposeCount { get; private set; }

        public AppleSmcServiceState GetState()
        {
            return _states.Dequeue();
        }

        public void Start()
        {
            StartCount++;
        }

        public void Stop()
        {
            StopCount++;
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class FakeAppleSmcTransportFactory : IAppleSmcTransportFactory
    {
        private readonly ISmcTransport? _transport;
        private readonly Exception? _openException;

        public FakeAppleSmcTransportFactory(ISmcTransport transport)
        {
            _transport = transport;
        }

        public FakeAppleSmcTransportFactory(Exception openException)
        {
            _openException = openException;
        }

        public int OpenCount { get; private set; }

        public ISmcTransport Open()
        {
            OpenCount++;

            if (_openException is not null)
            {
                throw _openException;
            }

            return _transport
                ?? throw new InvalidOperationException("No fake transport was configured.");
        }
    }

    private sealed class FakeSmcTransport : ISmcTransport
    {
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        public FakeSmcTransport()
        {
            SetUInt8("FNum", 2, 0x80);
            SetFloat32("F0Mx", 5616f, 0x85);
            SetFloat32("F1Mx", 5200f, 0x85);
            SetFloat32("F0Ac", 1840f, 0x84);
            SetFloat32("F1Ac", 1691f, 0x84);
            SetUInt8("F0Md", 0, 0xD0);
            SetUInt8("F1Md", 0, 0xD0);
            SetFloat32("F0Tg", 1900f, 0xD4);
            SetFloat32("F1Tg", 1760f, 0xD4);
        }

        public int ProtocolCalls { get; private set; }

        public int KeyInfoCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public bool IsDisposed { get; private set; }

        public Task<SmcTransportProtocol> GetProtocolAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProtocolCalls++;
            return Task.FromResult(SmcTransportProtocol.Mmio);
        }

        public Task<SmcKeyInfo> GetKeyInfoAsync(
            string key,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            KeyInfoCalls++;
            var entry = _entries[key];
            return Task.FromResult(new SmcKeyInfo(
                key,
                checked((byte)entry.Data.Length),
                entry.Type,
                entry.Attributes));
        }

        public Task<ReadOnlyMemory<byte>> ReadKeyAsync(
            string key,
            byte length,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            var entry = _entries[key];
            Assert.Equal(entry.Data.Length, length);
            return Task.FromResult<ReadOnlyMemory<byte>>(entry.Data);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }

        private void SetUInt8(string key, byte value, byte attributes)
        {
            _entries[key] = new Entry("ui8 ", attributes, [value]);
        }

        private void SetFloat32(string key, float value, byte attributes)
        {
            _entries[key] = new Entry("flt ", attributes, BitConverter.GetBytes(value));
        }

        private sealed record Entry(
            string Type,
            byte Attributes,
            byte[] Data);
    }
}
