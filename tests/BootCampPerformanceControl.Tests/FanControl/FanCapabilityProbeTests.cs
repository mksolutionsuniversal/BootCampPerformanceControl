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
        Assert.Equal(FanCapabilityFamily.PerFanModeFloat32, result.Family);
        Assert.Empty(result.Failures);
        Assert.True(result.Protocol.HasValue);
        Assert.Equal(SmcTransportProtocol.Mmio, result.Protocol.Value);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(12, transport.KeyInfoCalls);
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
        Assert.Equal(12, transport.KeyInfoCalls);
        Assert.Equal(9, transport.ReadCalls);
    }

    [Fact]
    public async Task ProbeAsync_UnverifiedProtocolRetainsDiagnosticsButDisablesWrites()
    {
        await using var transport = new FakeSmcTransport
        {
            Protocol = SmcTransportProtocol.Unknown
        };
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.True(result.IsReadSupported);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.Equal(FanCapabilityFamily.PerFanModeFloat32, result.Family);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("MMIO", StringComparison.Ordinal));
        Assert.True(result.Protocol.HasValue);
        Assert.Equal(SmcTransportProtocol.Unknown, result.Protocol.Value);
        Assert.Equal(1, transport.ProtocolCalls);
        Assert.Equal(12, transport.KeyInfoCalls);
        Assert.Equal(9, transport.ReadCalls);
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
    public async Task ProbeAsync_MixedFpe2SchemaCannotEnterPerFanWriteFamily()
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
            new[] { "FNum", "F0Mn", "F0Mx", "F0Ac", "F0Md", "F0Tg", "FS! " },
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
        Assert.Equal(FanCapabilityFamily.Unknown, result.Family);
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

    [Fact]
    public async Task ProbeAsync_ClassifiesOneFanGlobalMaskFpe2FromExactLiveFingerprint()
    {
        await using var transport = new FakeSmcTransport();
        transport.ConfigureGlobalMaskFpe2(1, 0x0000);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync("MacBookPro12,1", CancellationToken.None);

        Assert.True(result.IsReadSupported);
        Assert.True(result.IsHardwareSafetyGateSatisfied);
        Assert.Equal(FanCapabilityFamily.GlobalMaskFpe2, result.Family);
        Assert.Single(result.Snapshot!.Fans);
        Assert.Equal(6199f, result.Snapshot.Fans[0].Maximum.GetFpe2());
        Assert.Null(result.Snapshot.Fans[0].Mode);
        Assert.Equal((ushort)0, result.Snapshot.GlobalMode.Value!.GetUInt16BigEndian());
        Assert.Equal(
            SmcKeyObservationState.ConfirmedAbsent,
            result.Snapshot.Fans[0].ModeObservation.State);
    }

    [Fact]
    public async Task ProbeAsync_ConfirmedMissingGlobalModeCompletesPerFanFingerprint()
    {
        await using var transport = new FakeSmcTransport();
        var result = await CreateProbe(transport).ProbeAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.Equal(FanCapabilityFamily.PerFanModeFloat32, result.Family);
        Assert.True(result.IsHardwareSafetyGateSatisfied);
        Assert.Equal(
            SmcKeyObservationState.ConfirmedAbsent,
            result.Snapshot!.GlobalMode.State);
    }

    [Fact]
    public async Task ProbeAsync_GlobalModeReadFailureCannotSatisfyPerFanFingerprint()
    {
        await using var transport = new FakeSmcTransport();
        transport.FailKeyInfo("FS! ", new IOException("transient DeviceIoControl failure"));

        var result = await CreateProbe(transport).ProbeAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);
        var preparation = new FanOverridePreflightPolicy().PrepareMaximumSafeRpm(
            VerifiedHardwareModels.MacBookPro16_1,
            result);

        Assert.Equal(FanCapabilityFamily.Unknown, result.Family);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.False(preparation.IsAllowed);
        Assert.Equal(SmcKeyObservationState.ReadFailed, result.Snapshot!.GlobalMode.State);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("read failed", StringComparison.OrdinalIgnoreCase) &&
            failure.Contains("transient DeviceIoControl failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeAsync_ClassifiesTwoFanGlobalMaskFpe2UsingProvenMaskRange()
    {
        await using var transport = new FakeSmcTransport();
        transport.ConfigureGlobalMaskFpe2(2, 0x0003);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync("UnlistedIntelMac", CancellationToken.None);

        Assert.True(result.IsReadSupported);
        Assert.True(result.IsHardwareSafetyGateSatisfied);
        Assert.Equal(FanCapabilityFamily.GlobalMaskFpe2, result.Family);
        Assert.Equal(2, result.Snapshot!.Fans.Count);
    }

    [Fact]
    public async Task ProbeAsync_ModeReadFailureCannotSatisfyGlobalFingerprint()
    {
        await using var transport = new FakeSmcTransport();
        transport.ConfigureGlobalMaskFpe2(2, 0x0000);
        transport.FailKeyInfo("F0Md", new IOException("transient DeviceIoControl failure"));

        var result = await CreateProbe(transport).ProbeAsync(
            "UnlistedIntelMac",
            CancellationToken.None);
        var preparation = new FanOverridePreflightPolicy().PrepareMaximumSafeRpm(
            "UnlistedIntelMac",
            result);

        Assert.Equal(FanCapabilityFamily.Unknown, result.Family);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.False(preparation.IsAllowed);
        Assert.Equal(
            SmcKeyObservationState.ReadFailed,
            result.Snapshot!.Fans[0].ModeObservation.State);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("F0Md", StringComparison.Ordinal) &&
            failure.Contains("read failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProbeAsync_GlobalMaskTopologyBeyondProvenRangeIsReadOnly()
    {
        await using var transport = new FakeSmcTransport();
        transport.ConfigureGlobalMaskFpe2(3, 0x0007);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync("UnlistedIntelMac", CancellationToken.None);

        Assert.True(result.IsReadSupported);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.Equal(FanCapabilityFamily.GlobalMaskFpe2, result.Family);
        Assert.Equal((ushort)0x0007, result.Snapshot!.GlobalMode.Value!.GetUInt16BigEndian());
        Assert.Contains(result.Failures, failure =>
            failure.Contains("not verified for this topology", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProbeAsync_MixedSchemaIsUnknownAndRetainsReadOnlySnapshot()
    {
        await using var transport = new FakeSmcTransport();
        transport.SetFpe2Raw("F0Mx", 0x60DC, 0xC0);
        var probe = CreateProbe(transport);

        var result = await probe.ProbeAsync("UnlistedIntelMac", CancellationToken.None);

        Assert.False(result.IsReadSupported);
        Assert.False(result.IsHardwareSafetyGateSatisfied);
        Assert.Equal(FanCapabilityFamily.Unknown, result.Family);
        Assert.NotNull(result.Snapshot);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("Write capability not verified", StringComparison.OrdinalIgnoreCase));
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
        private readonly Dictionary<string, Exception> _keyInfoFailures = new(StringComparer.Ordinal);

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
            if (_keyInfoFailures.TryGetValue(key, out var failure))
            {
                throw failure;
            }

            if (!_entries.TryGetValue(key, out var entry))
            {
                throw new SmcKeyNotFoundException(key);
            }
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

        public void SetFpe2Raw(string key, ushort raw, byte attributes)
        {
            _entries[key] = new Entry(
                "fpe2",
                attributes,
                [checked((byte)(raw >> 8)), checked((byte)(raw & 0xFF))]);
        }

        public void ConfigureGlobalMaskFpe2(int fanCount, ushort mask)
        {
            _entries.Clear();
            SetUInt8("FNum", checked((byte)fanCount), 0x80);
            for (var index = 0; index < fanCount; index++)
            {
                SetFpe2Raw($"F{index}Mn", 0x144C, 0xC0);
                SetFpe2Raw($"F{index}Mx", checked((ushort)(0x60DC - index * 0x0400)), 0xC0);
                SetFpe2Raw($"F{index}Ac", 0x2404, 0x90);
                SetFpe2Raw($"F{index}Tg", mask == 0 ? (ushort)0x248C : checked((ushort)(0x60DC - index * 0x0400)), 0xD0);
            }

            _entries["FS! "] = new Entry(
                "ui16",
                0xC0,
                [checked((byte)(mask >> 8)), checked((byte)(mask & 0xFF))]);
        }

        public void SetFan(int index, float maximum, float actual, byte mode, float target)
        {
            SetFloat32($"F{index}Mx", maximum, 0x85);
            SetFloat32($"F{index}Ac", actual, 0x84);
            SetUInt8($"F{index}Md", mode, 0xD0);
            SetFloat32($"F{index}Tg", target, 0xD4);
        }

        public void FailKeyInfo(string key, Exception exception)
        {
            _keyInfoFailures[key] = exception;
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
