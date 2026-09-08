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

        IFanCapabilityFamilyStrategy strategy;
        try
        {
            strategy = FanCapabilityFamilyStrategies.Get(capability.Family);
        }
        catch (InvalidOperationException)
        {
            return FanOverridePreparationResult.Blocked(
                "Fan override is blocked because write capability is not verified for the observed family.");
        }

        if (!strategy.IsAppleAuto(capability))
        {
            return FanOverridePreparationResult.Blocked(
                "Fan override is blocked because Apple Auto must be active before this application can take ownership.");
        }

        return FanOverridePreparationResult.Allowed(
            strategy.CreateMaximumSafeRpmPlan(model, capability));
    }
}
