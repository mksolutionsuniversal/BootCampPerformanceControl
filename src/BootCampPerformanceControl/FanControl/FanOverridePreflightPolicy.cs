namespace BootCampPerformanceControl.FanControl;

internal sealed class FanOverridePreflightPolicy
{
    public FanOverridePreparationResult PrepareMaximumSafeRpm(
        string model,
        FanControlCapabilityResult capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(capability);

        if (!capability.IsReadSupported ||
            !capability.IsHardwareSafetyGateSatisfied ||
            capability.Snapshot is null)
        {
            return FanOverridePreparationResult.Blocked(
                "Fan override is blocked because the hardware safety gate is not satisfied.");
        }

        var snapshot = capability.Snapshot;
        if (snapshot.Fans.Any(fan => fan.Mode.GetUInt8() != 0))
        {
            return FanOverridePreparationResult.Blocked(
                "Fan override is blocked because every fan must be in Apple Auto before this application can take ownership.");
        }

        return FanOverridePreparationResult.Allowed(
            new FanMaximumSafeRpmPlan(
                model,
                snapshot.Fans.Select(fan => new FanMaximumSafeRpmTarget(
                    fan.Index,
                    fan.Maximum.GetFloat32()))));
    }
}
