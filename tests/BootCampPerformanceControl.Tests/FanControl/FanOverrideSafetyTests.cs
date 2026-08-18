using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.FanControl;

public sealed class FanOverrideSafetyTests
{
    private const string Model = VerifiedHardwareModels.MacBookPro16_1;

    [Fact]
    public void Preflight_AllowsVerifiedAppleAutoAndUsesReportedMaxima()
    {
        var policy = new FanOverridePreflightPolicy();
        var capability = CreateCapability();

        var result = policy.PrepareMaximumSafeRpm(Model, capability);

        Assert.True(result.IsAllowed);
        Assert.Null(result.FailureReason);
        Assert.NotNull(result.Plan);
        Assert.Equal(Model, result.Plan.Model);
        Assert.Equal(5616f, result.Plan.Fan0TargetRpm);
        Assert.Equal(5200f, result.Plan.Fan1TargetRpm);
    }

    [Fact]
    public void Preflight_BlocksWhenHardwareSafetyGateIsNotSatisfied()
    {
        var policy = new FanOverridePreflightPolicy();
        var capability = FanControlCapabilityResult.Rejected(
            SmcTransportProtocol.Mmio,
            "test rejection");

        var result = policy.PrepareMaximumSafeRpm(Model, capability);

        Assert.False(result.IsAllowed);
        Assert.Null(result.Plan);
        Assert.Contains("safety gate", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public void Preflight_BlocksWhenEitherFanIsAlreadyManual(byte fan0Mode, byte fan1Mode)
    {
        var policy = new FanOverridePreflightPolicy();
        var capability = CreateCapability(fan0Mode: fan0Mode, fan1Mode: fan1Mode);

        var result = policy.PrepareMaximumSafeRpm(Model, capability);

        Assert.False(result.IsAllowed);
        Assert.Null(result.Plan);
        Assert.Contains("Apple Auto", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnershipMarker_IsCreatedFromApprovedPlan()
    {
        var plan = new FanMaximumSafeRpmPlan(Model, 5616f, 5200f);
        var timestamp = new DateTimeOffset(2026, 8, 18, 18, 0, 0, TimeSpan.Zero);

        var marker = FanOverrideOwnershipMarker.FromPlan(plan, timestamp);

        Assert.Equal(Model, marker.Model);
        Assert.Equal(5616f, marker.Fan0ExpectedTargetRpm);
        Assert.Equal(5200f, marker.Fan1ExpectedTargetRpm);
        Assert.Equal(timestamp, marker.CreatedAtUtc);
    }

    [Fact]
    public void Recovery_PermitsAppleAutoRestoreOnlyWhenManualStateMatchesMarker()
    {
        var policy = new FanOverrideRecoveryPolicy();
        var marker = CreateMarker();
        var capability = CreateCapability(
            fan0Mode: 1,
            fan1Mode: 1,
            fan0Target: 5616f,
            fan1Target: 5200f);

        var result = policy.Evaluate(Model, marker, capability);

        Assert.Equal(FanOverrideRecoveryAction.RestoreAppleAuto, result.Action);
        Assert.Contains("permitted", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recovery_DoesNothingWhenFansAreAlreadyAppleAuto()
    {
        var policy = new FanOverrideRecoveryPolicy();
        var marker = CreateMarker();
        var capability = CreateCapability();

        var result = policy.Evaluate(Model, marker, capability);

        Assert.Equal(FanOverrideRecoveryAction.None, result.Action);
        Assert.Contains("already", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void Recovery_BlocksMixedOwnershipModes(byte fan0Mode, byte fan1Mode)
    {
        var policy = new FanOverrideRecoveryPolicy();
        var marker = CreateMarker();
        var capability = CreateCapability(
            fan0Mode: fan0Mode,
            fan1Mode: fan1Mode,
            fan0Target: 5616f,
            fan1Target: 5200f);

        var result = policy.Evaluate(Model, marker, capability);

        Assert.Equal(FanOverrideRecoveryAction.Blocked, result.Action);
        Assert.Contains("modes", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recovery_BlocksWhenAnotherActorChangesTarget()
    {
        var policy = new FanOverrideRecoveryPolicy();
        var marker = CreateMarker();
        var capability = CreateCapability(
            fan0Mode: 1,
            fan1Mode: 1,
            fan0Target: 5000f,
            fan1Target: 5200f);

        var result = policy.Evaluate(Model, marker, capability);

        Assert.Equal(FanOverrideRecoveryAction.Blocked, result.Action);
        Assert.Contains("targets", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recovery_BlocksWhenMaximumRpmNoLongerMatchesMarker()
    {
        var policy = new FanOverrideRecoveryPolicy();
        var marker = CreateMarker();
        var capability = CreateCapability(
            fan0Mode: 1,
            fan1Mode: 1,
            fan0Maximum: 5500f,
            fan0Target: 5616f,
            fan1Target: 5200f);

        var result = policy.Evaluate(Model, marker, capability);

        Assert.Equal(FanOverrideRecoveryAction.Blocked, result.Action);
        Assert.Contains("maximum RPM", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recovery_BlocksMarkerFromDifferentModel()
    {
        var policy = new FanOverrideRecoveryPolicy();
        var marker = CreateMarker() with { Model = "MacBookPro14,3" };
        var capability = CreateCapability(
            fan0Mode: 1,
            fan1Mode: 1,
            fan0Target: 5616f,
            fan1Target: 5200f);

        var result = policy.Evaluate(Model, marker, capability);

        Assert.Equal(FanOverrideRecoveryAction.Blocked, result.Action);
        Assert.Contains("different Mac model", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recovery_BlocksWhenHardwareSafetyGateIsNotSatisfied()
    {
        var policy = new FanOverrideRecoveryPolicy();
        var marker = CreateMarker();
        var capability = FanControlCapabilityResult.Rejected(
            SmcTransportProtocol.Mmio,
            "test rejection");

        var result = policy.Evaluate(Model, marker, capability);

        Assert.Equal(FanOverrideRecoveryAction.Blocked, result.Action);
        Assert.Contains("safety gate", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static FanOverrideOwnershipMarker CreateMarker()
    {
        return new FanOverrideOwnershipMarker(
            Model,
            5616f,
            5200f,
            DateTimeOffset.UtcNow);
    }

    private static FanControlCapabilityResult CreateCapability(
        byte fan0Mode = 0,
        byte fan1Mode = 0,
        float fan0Maximum = 5616f,
        float fan1Maximum = 5200f,
        float fan0Target = 1836f,
        float fan1Target = 1700f)
    {
        var snapshot = new FanSmcSnapshot(
            UInt8("FNum", 2, 0x80),
            Float32("F0Mx", fan0Maximum, 0x85),
            Float32("F1Mx", fan1Maximum, 0x85),
            Float32("F0Ac", 1837f, 0x84),
            Float32("F1Ac", 1701f, 0x84),
            UInt8("F0Md", fan0Mode, 0xD0),
            UInt8("F1Md", fan1Mode, 0xD0),
            Float32("F0Tg", fan0Target, 0xD4),
            Float32("F1Tg", fan1Target, 0xD4));

        return new FanControlCapabilityResult(
            IsReadSupported: true,
            IsHardwareSafetyGateSatisfied: true,
            Array.Empty<string>(),
            SmcTransportProtocol.Mmio,
            snapshot);
    }

    private static SmcValue UInt8(string key, byte value, byte attributes)
    {
        return new SmcValue(
            new SmcKeyInfo(key, 1, "ui8 ", attributes),
            [value]);
    }

    private static SmcValue Float32(string key, float value, byte attributes)
    {
        return new SmcValue(
            new SmcKeyInfo(key, 4, "flt ", attributes),
            BitConverter.GetBytes(value));
    }
}
