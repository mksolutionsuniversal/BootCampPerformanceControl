namespace BootCampPerformanceControl.FanControl;

internal sealed class FanOverrideRecoveryPolicy
{
    private const float RpmComparisonTolerance = 1f;

    public FanOverrideRecoveryDecision Evaluate(
        string currentModel,
        FanOverrideOwnershipMarker marker,
        FanControlCapabilityResult capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentModel);
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(capability);

        if (!string.Equals(currentModel, marker.Model, StringComparison.Ordinal))
        {
            return Blocked("The ownership marker belongs to a different Mac model.");
        }

        if (!capability.IsReadSupported ||
            !capability.IsHardwareSafetyGateSatisfied ||
            capability.Snapshot is null)
        {
            return Blocked("Recovery is blocked because the hardware safety gate is not satisfied.");
        }

        var snapshot = capability.Snapshot;
        var fan0Mode = snapshot.Fan0Mode.GetUInt8();
        var fan1Mode = snapshot.Fan1Mode.GetUInt8();

        if (fan0Mode == 0 && fan1Mode == 0)
        {
            return new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.None,
                "Both fans are already in Apple Auto. The stale ownership marker can be cleared.");
        }

        if (fan0Mode != 1 || fan1Mode != 1)
        {
            return Blocked(
                "Recovery is blocked because the current fan modes no longer match the application-owned manual state.");
        }

        var fan0Target = snapshot.Fan0Target.GetFloat32();
        var fan1Target = snapshot.Fan1Target.GetFloat32();
        var fan0Maximum = snapshot.Fan0Maximum.GetFloat32();
        var fan1Maximum = snapshot.Fan1Maximum.GetFloat32();

        if (!ApproximatelyEqual(fan0Target, marker.Fan0ExpectedTargetRpm) ||
            !ApproximatelyEqual(fan1Target, marker.Fan1ExpectedTargetRpm))
        {
            return Blocked(
                "Recovery is blocked because the current fan targets do not match the application ownership marker.");
        }

        if (!ApproximatelyEqual(fan0Maximum, marker.Fan0ExpectedTargetRpm) ||
            !ApproximatelyEqual(fan1Maximum, marker.Fan1ExpectedTargetRpm))
        {
            return Blocked(
                "Recovery is blocked because the current maximum RPM values do not match the application ownership marker.");
        }

        return new FanOverrideRecoveryDecision(
            FanOverrideRecoveryAction.RestoreAppleAuto,
            "The current manual/max state matches the application ownership marker. Apple Auto recovery is permitted.");
    }

    private static bool ApproximatelyEqual(float left, float right)
    {
        return float.IsFinite(left) &&
               float.IsFinite(right) &&
               MathF.Abs(left - right) <= RpmComparisonTolerance;
    }

    private static FanOverrideRecoveryDecision Blocked(string reason)
    {
        return new FanOverrideRecoveryDecision(
            FanOverrideRecoveryAction.Blocked,
            reason);
    }
}
