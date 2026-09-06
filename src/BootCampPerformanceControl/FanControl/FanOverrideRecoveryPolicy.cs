namespace BootCampPerformanceControl.FanControl;

internal sealed class FanOverrideRecoveryPolicy
{
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

        if (capability.Family != marker.Family)
        {
            return Blocked(
                "Recovery is blocked because the live capability family differs from the ownership marker family.");
        }

        var snapshot = capability.Snapshot;
        if (snapshot.Fans.Count != marker.Targets.Count ||
            !snapshot.Fans.Select(fan => fan.Index)
                .SequenceEqual(marker.Targets.Select(target => target.Index)))
        {
            return Blocked(
                "Recovery is blocked because the current fan topology does not match the application ownership marker.");
        }

        if (!HasValidFamilySpecificMarkerState(marker))
        {
            return Blocked(
                "Recovery is blocked because the ownership marker contains invalid family-specific safety state.");
        }

        IFanCapabilityFamilyStrategy strategy;
        try
        {
            strategy = FanCapabilityFamilyStrategies.Get(marker.Family);
        }
        catch (InvalidOperationException)
        {
            return Blocked(
                "Recovery is blocked because the ownership marker does not identify a bounded writer family.");
        }

        if (strategy.IsAppleAuto(capability))
        {
            return new FanOverrideRecoveryDecision(
                FanOverrideRecoveryAction.None,
                "Every owned fan is already in Apple Auto. The stale ownership marker can be cleared.");
        }

        FanMaximumSafeRpmPlan expectedPlan;
        try
        {
            expectedPlan = new FanMaximumSafeRpmPlan(
                marker.Model,
                marker.Family,
                marker.Targets.Select(target =>
                    new FanMaximumSafeRpmTarget(
                        target.Index,
                        target.ExpectedTargetRpm)
                    {
                        ExactTargetPayload = target.ExpectedTargetRawHex is null
                            ? ReadOnlyMemory<byte>.Empty
                            : Convert.FromHexString(target.ExpectedTargetRawHex)
                    }));
        }
        catch (FormatException)
        {
            return Blocked(
                "Recovery is blocked because the ownership marker contains malformed raw target state.");
        }

        if (!strategy.IsManualMaximum(capability, expectedPlan))
        {
            return Blocked(
                "Recovery is blocked because the current family-specific modes, targets, or maximum RPM values no longer match the application ownership marker.");
        }

        return new FanOverrideRecoveryDecision(
            FanOverrideRecoveryAction.RestoreAppleAuto,
            "The current manual/max state matches the application ownership marker. Apple Auto recovery is permitted.");
    }

    private static FanOverrideRecoveryDecision Blocked(string reason)
    {
        return new FanOverrideRecoveryDecision(
            FanOverrideRecoveryAction.Blocked,
            reason);
    }

    private static bool HasValidFamilySpecificMarkerState(
        FanOverrideOwnershipMarker marker)
    {
        if (marker.Family == FanCapabilityFamily.PerFanModeFloat32)
        {
            return marker.ExpectedGlobalModeMask is null &&
                marker.Targets.All(target => target.ExpectedTargetRawHex is null);
        }

        if (marker.Family != FanCapabilityFamily.GlobalMaskFpe2)
        {
            return false;
        }

        ushort expectedMask;
        try
        {
            expectedMask = GlobalMaskFpe2Strategy.GetManualMask(marker.Targets.Count);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return marker.ExpectedGlobalModeMask == expectedMask &&
            marker.Targets.All(target =>
            {
                try
                {
                    if (target.ExpectedTargetRawHex is not { Length: 4 } rawHex)
                    {
                        return false;
                    }

                    var raw = Convert.FromHexString(rawHex);
                    var decodedRpm = ((raw[0] << 8) | raw[1]) / 4f;
                    return raw.Length == 2 && decodedRpm == target.ExpectedTargetRpm;
                }
                catch (Exception exception) when (
                    exception is FormatException or IndexOutOfRangeException)
                {
                    return false;
                }
            });
    }
}
