using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.FanControl;

public sealed class FanCapabilityProbeTests
{
    [Fact]
    public async Task ProbeAsync_AcceptsVerifiedMacBookPro16_1Snapshot()
    {
        await using var transport = new FakeSmcTransport();
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.True(result.IsReadSupported);
        Assert.True(result.IsHardwareSafetyGateSatisfied);
        Assert.Empty(result.Failures);
        Assert.True(result.Protocol.HasValue);
        Assert.Equal(SmcTransportProtocol.Mmio, result.Protocol.Value);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(9, transport.KeyInfoCalls);
        Assert.Equal(9, transport.ReadCalls);
    }

    [Fact]
    public async Task ProbeAsync_ValidUnknownModelIsReadAndWriteFamilySupported()
    {
        await using var transport = new FakeSmcTransport();
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync("MacBookPro14,3", CancellationToken.None);

        Assert.True(result.IsReadSupported);
        Assert.True(result.IsHardwareSafetyGateSatisfied);
        Assert.Empty(result.Failures);
        Assert.Equal(1, transport.ProtocolCalls);
        Assert.Equal(9, transport.KeyInfoCalls);
        Assert.Equal(9, transport.ReadCalls);
    }

    [Fact]
    public async Task ProbeAsync_RejectsNonMmioProtocolBeforeKeyReads()
    {
        await using var transport = new FakeSmcTransport
        {
            Protocol = SmcTransportProtocol.Unknown
        };
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.False(result.IsReadSupported);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.True(result.Protocol.HasValue);
        Assert.Equal(SmcTransportProtocol.Unknown, result.Protocol.Value);
        Assert.Equal(1, transport.ProtocolCalls);
        Assert.Equal(0, transport.KeyInfoCalls);
        Assert.Equal(0, transport.ReadCalls);
    }

    [Fact]
    public async Task ProbeAsync_ThreeFanTopologyIsReadAndWriteFamilySupported()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetUInt8("FNum", 3, 0x80);
        transport.SetFan(2, 4800f, 1500f, 0, 1500f);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.True(result.IsReadSupported);
        Assert.True(result.IsHardwareSafetyGateSatisfied);
        Assert.Equal(3, result.Snapshot?.Fans.Count);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task ProbeAsync_RejectsUnexpectedMetadataAttributes()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetFloat32("F0Mx", 5616f, 0x84);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.False(result.IsReadSupported);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.Contains(result.Failures, failure => failure.Contains("F0Mx", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("metadata mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeAsync_T1LikeFpe2RpmSchemaCannotEnterT2WriteFamily()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetFpe2("F0Mx", 5616, 0x85);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync("MacBookPro14,3", CancellationToken.None);

        Assert.False(result.IsReadSupported);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("F0Mx", StringComparison.Ordinal)
            && failure.Contains("type='fpe2'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeAsync_DifferentValidMaximumDoesNotDependOnLegacyModelEnvelope()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetFloat32("F0Mx", 9000f, 0x85);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.True(result.IsReadSupported);
        Assert.True(result.IsHardwareSafetyGateSatisfied);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task ProbeAsync_OneFanReadsOnlyDiscoveredFanKeys()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetUInt8("FNum", 1, 0x80);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync("MacBookPro14,3", CancellationToken.None);

        Assert.True(result.IsReadSupported);
        Assert.True(result.IsHardwareSafetyGateSatisfied);
        Assert.Single(result.Snapshot!.Fans);
        Assert.Equal(
            new[] { "FNum", "F0Mx", "F0Ac", "F0Md", "F0Tg" },
            transport.RequestedKeys);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(10000.01f)]
    public async Task ProbeAsync_InvalidMaximumFailsFamilyValidation(float maximum)
    {
        await using var transport = new FakeSmcTransport();
        transport.SetFloat32("F0Mx", maximum, 0x85);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync("MacBookPro14,3", CancellationToken.None);

        Assert.False(result.IsReadSupported);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.Contains(result.Failures, failure => failure.Contains("invalid maximum RPM", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeAsync_MaximumAtCorruptionCeilingIsAccepted()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetFloat32("F0Mx", 10000f, 0x85);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync("MacBookPro14,3", CancellationToken.None);

        Assert.True(result.IsReadSupported);
        Assert.True(result.IsHardwareSafetyGateSatisfied);
    }

    [Fact]
    public async Task ProbeAsync_ZeroFansIsValidPassiveTopologyAndReadsNoFanKeys()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetUInt8("FNum", 0, 0x80);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync("MacBookPro14,3", CancellationToken.None);

        Assert.True(result.IsReadSupported);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.Empty(result.Snapshot!.Fans);
        Assert.Equal(new[] { "FNum" }, transport.RequestedKeys);
    }

    [Fact]
    public async Task ProbeAsync_UnrepresentableFanCountFailsClosedBeforeFanKeyReads()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetUInt8("FNum", 11, 0x80);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync("MacBookPro14,3", CancellationToken.None);

        Assert.False(result.IsReadSupported);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.Contains(result.Failures, failure => failure.Contains("at most 10", StringComparison.Ordinal));
        Assert.Equal(new[] { "FNum" }, transport.RequestedKeys);
    }

    [Fact]
    public async Task ProbeAsync_InvalidFanCountMetadataFailsClosedBeforeFanKeyReads()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetUInt8("FNum", 2, 0x81);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync("MacBookPro16,1", CancellationToken.None);

        Assert.False(result.IsReadSupported);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.Contains(result.Failures, failure => failure.Contains("FNum", StringComparison.Ordinal));
        Assert.Equal(new[] { "FNum" }, transport.RequestedKeys);
    }

    [Fact]
    public async Task ProbeAsync_RejectsImplausibleRuntimeRpm()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetFloat32("F0Ac", 7000f, 0x84);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.False(result.IsReadSupported);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.Contains(result.Failures, failure => failure.Contains("implausible RPM", StringComparison.Ordinal));
    }

    private static FanCapabilityProbe CreateProbe(FakeSmcTransport transport)
    {
        return new FanCapabilityProbe(
            new AppleSmcProtocol(transport),
            new FanSafetyPolicy());
    }

    private sealed class FakeSmcTransport : ISmcTransport
    {
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        public FakeSmcTransport()
        {
            SetUInt8("FNum", 2, 0x80);
            SetFloat32("F0Mx", 5616f, 0x85);
            SetFloat32("F1Mx", 5200f, 0x85);
            SetFloat32("F0Ac", 1839.983f, 0x84);
            SetFloat32("F1Ac", 1691.173f, 0x84);
            SetUInt8("F0Md", 0, 0xD0);
            SetUInt8("F1Md", 0, 0xD0);
            SetFloat32("F0Tg", 1836f, 0xD4);
            SetFloat32("F1Tg", 1700f, 0xD4);
        }

        public SmcTransportProtocol Protocol { get; init; } = SmcTransportProtocol.Mmio;

        public int ProtocolCalls { get; private set; }

        public int KeyInfoCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public List<string> RequestedKeys { get; } = new();

        public Task<SmcTransportProtocol> GetProtocolAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProtocolCalls++;
            return Task.FromResult(Protocol);
        }

        public Task<SmcKeyInfo> GetKeyInfoAsync(
            string key,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            KeyInfoCalls++;
            RequestedKeys.Add(key);
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
            Assert.Equal(entry.Raw.Length, (int)length);
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

        public void SetFpe2(string key, ushort value, byte attributes)
        {
            _entries[key] = new Entry(
                "fpe2",
                attributes,
                [checked((byte)(value >> 8)), checked((byte)(value & 0xFF))]);
        }

        public void SetFan(int index, float maximum, float actual, byte mode, float target)
        {
            SetFloat32($"F{index}Mx", maximum, 0x85);
            SetFloat32($"F{index}Ac", actual, 0x84);
            SetUInt8($"F{index}Md", mode, 0xD0);
            SetFloat32($"F{index}Tg", target, 0xD4);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        private sealed record Entry(
            string Type,
            byte Attributes,
            byte[] Raw);
    }
}
