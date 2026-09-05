using BootCampPerformanceControl.FanControl;

namespace BootCampPerformanceControl.Profiles;

internal sealed class GamingOptimisedSessionStateEvaluator
{
    public GamingOptimisedSessionState Evaluate(
        ProcessorProfileState processorState,
        bool hasOriginalRestoreSnapshot,
        FanRecoveryState fanRecoveryState,
        FanControlStatus fanStatus)
    {
        ArgumentNullException.ThrowIfNull(fanStatus);

        if (fanRecoveryState is FanRecoveryState.PreviousSessionRecoveryPending
            or FanRecoveryState.RecoveryBlocked
            or FanRecoveryState.InspectionFailed)
        {
            return GamingOptimisedSessionState.FanRecoveryPendingOrUnsafe;
        }

        if (processorState == ProcessorProfileState.Unknown)
        {
            return GamingOptimisedSessionState.Unknown;
        }

        if (fanRecoveryState == FanRecoveryState.CurrentSessionOverrideActive)
        {
            return hasOriginalRestoreSnapshot
                && processorState == ProcessorProfileState.GamingOptimisedDetected
                && IsVerifiedMaximumSafeRpm(fanStatus)
                    ? GamingOptimisedSessionState.Full
                    : GamingOptimisedSessionState.FanRecoveryPendingOrUnsafe;
        }

        if (!hasOriginalRestoreSnapshot)
        {
            return GamingOptimisedSessionState.NoActiveSession;
        }

        return processorState == ProcessorProfileState.GamingOptimisedDetected
            ? GamingOptimisedSessionState.PartialCpuOnly
            : GamingOptimisedSessionState.Other;
    }

    internal static bool IsVerifiedAppleAuto(FanControlStatus fanStatus)
    {
        ArgumentNullException.ThrowIfNull(fanStatus);

        return fanStatus.IsAvailable
            && fanStatus.Fan0?.Mode == FanOperatingMode.AppleAuto
            && fanStatus.Fan1?.Mode == FanOperatingMode.AppleAuto;
    }

    private static bool IsVerifiedMaximumSafeRpm(FanControlStatus fanStatus)
    {
        return fanStatus.IsAvailable
            && fanStatus.WriteControlState == FanWriteControlState.MaximumSafeRpmDetected
            && fanStatus.Fan0?.Mode == FanOperatingMode.Manual
            && fanStatus.Fan1?.Mode == FanOperatingMode.Manual;
    }
}
