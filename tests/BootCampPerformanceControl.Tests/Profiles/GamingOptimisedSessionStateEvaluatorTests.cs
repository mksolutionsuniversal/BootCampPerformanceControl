using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.Profiles;

namespace BootCampPerformanceControl.Tests.Profiles;

public sealed class GamingOptimisedSessionStateEvaluatorTests
{
    private readonly GamingOptimisedSessionStateEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_SnapshotGamingCpuNoMarkerAppleAuto_ReturnsPartialCpuOnly()
    {
        var state = _evaluator.Evaluate(
            ProcessorProfileState.GamingOptimisedDetected,
            hasOriginalRestoreSnapshot: true,
            FanRecoveryState.None,
            AppleAutoStatus());

        Assert.Equal(GamingOptimisedSessionState.PartialCpuOnly, state);
    }

    [Fact]
    public void Evaluate_SnapshotGamingCpuOwnedVerifiedMaximum_ReturnsFull()
    {
        var state = _evaluator.Evaluate(
            ProcessorProfileState.GamingOptimisedDetected,
            hasOriginalRestoreSnapshot: true,
            FanRecoveryState.CurrentSessionOverrideActive,
            MaximumSafeRpmStatus());

        Assert.Equal(GamingOptimisedSessionState.Full, state);
    }

    [Fact]
    public void Evaluate_NoSnapshotNormalCpuAppleAuto_IsNotPartial()
    {
        var state = _evaluator.Evaluate(
            ProcessorProfileState.Other,
            hasOriginalRestoreSnapshot: false,
            FanRecoveryState.None,
            AppleAutoStatus());

        Assert.Equal(GamingOptimisedSessionState.NoActiveSession, state);
        Assert.NotEqual(GamingOptimisedSessionState.PartialCpuOnly, state);
    }

    [Fact]
    public void Evaluate_RecoveryContext_IsNeverPartial()
    {
        var recoveryStates = new[]
        {
            FanRecoveryState.PreviousSessionRecoveryPending,
            FanRecoveryState.RecoveryBlocked,
            FanRecoveryState.InspectionFailed
        };

        foreach (var recoveryState in recoveryStates)
        {
            var state = _evaluator.Evaluate(
                ProcessorProfileState.GamingOptimisedDetected,
                hasOriginalRestoreSnapshot: true,
                recoveryState,
                AppleAutoStatus());

            Assert.Equal(GamingOptimisedSessionState.FanRecoveryPendingOrUnsafe, state);
            Assert.NotEqual(GamingOptimisedSessionState.PartialCpuOnly, state);
        }
    }

    private static FanControlStatus AppleAutoStatus()
    {
        return new FanControlStatus(
            FanBackendState.Running,
            FanSafetyState.ReadOnlyVerified,
            [
                new FanChannelReading(0, new FanReading(1800f, 5321.25f, FanOperatingMode.AppleAuto)),
                new FanChannelReading(1, new FanReading(1700f, 4789.5f, FanOperatingMode.AppleAuto))
            ],
            "Verified Apple Auto.",
            FanWriteControlState.Available);
    }

    private static FanControlStatus MaximumSafeRpmStatus()
    {
        return new FanControlStatus(
            FanBackendState.Running,
            FanSafetyState.ReadOnlyVerified,
            [
                new FanChannelReading(0, new FanReading(5321.25f, 5321.25f, FanOperatingMode.Manual)),
                new FanChannelReading(1, new FanReading(4789.5f, 4789.5f, FanOperatingMode.Manual))
            ],
            "Verified Maximum Safe RPM.",
            FanWriteControlState.MaximumSafeRpmDetected);
    }
}
