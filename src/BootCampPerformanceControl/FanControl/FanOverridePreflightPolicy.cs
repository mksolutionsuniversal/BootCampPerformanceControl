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
        var fan0Mode = snapshot.Fan0Mode.GetUInt8();
        var fan1Mode = snapshot.Fan1Mode.GetUInt8();

        if (fan0Mode != 0 || fan1Mode != 0)
        {
            return FanOverridePreparationResult.Blocked(
                "Fan override is blocked because both fans must be in Apple Auto before this application can take ownership.");
        }

        var fan0Maximum = snapshot.Fan0Maximum.GetFloat32();
        var fan1Maximum = snapshot.Fan1Maximum.GetFloat32();

        return FanOverridePreparationResult.Allowed(
            new FanMaximumSafeRpmPlan(
                model,
                fan0Maximum,
                fan1Maximum));
    }
}
