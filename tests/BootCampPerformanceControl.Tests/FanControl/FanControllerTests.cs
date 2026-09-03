using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.FanControl;

public sealed class FanControllerTests
{
    [Fact]
    public void UnavailableStatus_UsesEncodingSafeEmDashPlaceholders()
    {
        var status = FanControlStatus.CreateUnavailable(
            FanBackendState.NotInstalled,
            FanSafetyState.MonitoringUnavailable,
            "Unavailable in test.");

        Assert.Equal("\u2014", status.Fan0DisplayText);
        Assert.Equal("\u2014", status.Fan1DisplayText);
        Assert.Equal("\u2014", status.ModeDisplayText);
    }

    [Fact]
    public async Task ReadStatusAsync_ReturnsVerifiedMonitoringAndWriteCapability()
    {
        await using var transport = new FakeSmcTransport();
        var controller = CreateController(transport);

        var result = await controller.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.True(result.Status.IsAvailable);
        Assert.Equal(FanBackendState.Running, result.Status.BackendState);
        Assert.Equal(FanSafetyState.ReadOnlyVerified, result.Status.SafetyState);
        Assert.Equal(FanWriteControlState.Available, result.Status.WriteControlState);
        Assert.Equal(new FanReading(1840f, 5616f, FanOperatingMode.AppleAuto), result.Status.Fan0);
        Assert.Equal(new FanReading(1691f, 5200f, FanOperatingMode.AppleAuto), result.Status.Fan1);
        Assert.True(result.Status.IsWriteControlEnabled);
        Assert.Equal("Read-only monitoring verified", result.Status.SafetyDisplayText);
        Assert.Equal("Available (verified MacBookPro16,1)", result.Status.WriteControlDisplayText);
        Assert.True(result.Capability.IsReadSupported);
        Assert.True(result.Capability.IsHardwareSafetyGateSatisfied);
        Assert.Contains("read-only monitoring verified", result.Status.DisplayText, StringComparison.Ordinal);
        Assert.Contains("Fan 0: 1840 / 5616 RPM (Apple Auto)", result.Status.DisplayText, StringComparison.Ordinal);
        Assert.Contains("Fan 1: 1691 / 5200 RPM (Apple Auto)", result.Status.DisplayText, StringComparison.Ordinal);
        Assert.Contains("Write control: Available (verified MacBookPro16,1)", result.Status.DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadStatusAsync_ReportsUnverifiedModelWithoutReadingKeys()
    {
        await using var transport = new FakeSmcTransport();
        var controller = CreateController(transport);

        var result = await controller.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro14_3,
            CancellationToken.None);

        Assert.False(result.Status.IsAvailable);
        Assert.Equal("Disabled (write capability not verified)", result.Status.WriteControlDisplayText);
        Assert.False(result.Capability.IsReadSupported);
        Assert.False(result.Capability.IsHardwareSafetyGateSatisfied);
        Assert.Contains("read-only unavailable", result.Status.DisplayText, StringComparison.Ordinal);
        Assert.Contains("not verified", result.Status.DisplayText, StringComparison.Ordinal);
        Assert.Equal(0, transport.KeyInfoCalls);
        Assert.Equal(0, transport.ReadCalls);
    }

    [Fact]
    public async Task ReadStatusAsync_ReportsManualModeWithoutChangingIt()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetUInt8("F0Md", 1, 0xD0);
        transport.SetUInt8("F1Md", 1, 0xD0);
        var controller = CreateController(transport);

        var result = await controller.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.True(result.Status.IsAvailable);
        Assert.Equal(FanWriteControlState.ManualModeDetected, result.Status.WriteControlState);
        Assert.False(result.Status.IsWriteControlEnabled);
        Assert.Equal("Manual mode detected", result.Status.WriteControlDisplayText);
        Assert.Equal(FanOperatingMode.Manual, result.Status.Fan0?.Mode);
        Assert.Equal(FanOperatingMode.Manual, result.Status.Fan1?.Mode);
        Assert.Contains("Fan 0: 1840 / 5616 RPM (Manual)", result.Status.DisplayText, StringComparison.Ordinal);
        Assert.Contains("Fan 1: 1691 / 5200 RPM (Manual)", result.Status.DisplayText, StringComparison.Ordinal);
        Assert.Equal(9, transport.KeyInfoCalls);
        Assert.Equal(9, transport.ReadCalls);
    }

    [Fact]
    public async Task ReadStatusAsync_ReportsMaximumSafeRpmManualOverride()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetUInt8("F0Md", 1, 0xD0);
        transport.SetUInt8("F1Md", 1, 0xD0);
        transport.SetFloat32("F0Tg", 5616f, 0xD4);
        transport.SetFloat32("F1Tg", 5200f, 0xD4);
        var controller = CreateController(transport);

        var result = await controller.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.Equal(FanWriteControlState.MaximumSafeRpmDetected, result.Status.WriteControlState);
        Assert.False(result.Status.IsWriteControlEnabled);
        Assert.Equal("Maximum Safe RPM detected (Manual mode)", result.Status.WriteControlDisplayText);
        Assert.Contains("Write control: Maximum Safe RPM detected (Manual mode)", result.Status.DisplayText, StringComparison.Ordinal);
    }

    private static FanController CreateController(FakeSmcTransport transport)
    {
        return new FanController(
            new FanCapabilityProbe(
                new AppleSmcProtocol(transport),
                new FanSafetyPolicy()));
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

        public int KeyInfoCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public Task<SmcTransportProtocol> GetProtocolAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SmcTransportProtocol.Mmio);
        }

        public Task<SmcKeyInfo> GetKeyInfoAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            KeyInfoCalls++;
            var entry = _entries[key];
            return Task.FromResult(new SmcKeyInfo(
                key,
                checked((byte)entry.Raw.Length),
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
            Assert.Equal(entry.Raw.Length, length);
            return Task.FromResult<ReadOnlyMemory<byte>>(entry.Raw);
        }

        public void SetUInt8(string key, byte value, byte attributes)
        {
            _entries[key] = new Entry("ui8 ", attributes, [value]);
        }

        public void SetFloat32(string key, float value, byte attributes)
        {
            _entries[key] = new Entry("flt ", attributes, BitConverter.GetBytes(value));
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        private sealed record Entry(string Type, byte Attributes, byte[] Raw);
    }
}
