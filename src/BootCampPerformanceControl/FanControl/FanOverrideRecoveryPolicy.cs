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
        if (snapshot.Fans.Count != marker.Targets.Count ||
            !snapshot.Fans.Select(fan => fan.Index)
                .SequenceEqual(marker.Targets.Select(target => target.Index)))
        {
            return Blocked(
                "Recovery is blocked because the current fan topology does not match the application ownership marker.");
        }

        if (snapshot.Fans.All(fan => fan.Mode.GetUInt8() == 0))
        {
            return new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.None,
                "Every owned fan is already in Apple Auto. The stale ownership marker can be cleared.");
        }

        if (!snapshot.Fans.All(fan => fan.Mode.GetUInt8() == 1))
        {
            return Blocked(
                "Recovery is blocked because the current fan modes no longer match the application-owned manual state.");
        }

        for (var position = 0; position < snapshot.Fans.Count; position++)
        {
            var fan = snapshot.Fans[position];
            var expected = marker.Targets[position];

            if (!ApproximatelyEqual(fan.Target.GetFloat32(), expected.ExpectedTargetRpm))
            {
                return Blocked(
                    "Recovery is blocked because the current fan targets do not match the application ownership marker.");
            }

            if (!ApproximatelyEqual(fan.Maximum.GetFloat32(), expected.ExpectedTargetRpm))
            {
                return Blocked(
                    "Recovery is blocked because the current maximum RPM values do not match the application ownership marker.");
            }
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
