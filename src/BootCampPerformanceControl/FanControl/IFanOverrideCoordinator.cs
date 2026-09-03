namespace BootCampPerformanceControl.FanControl;

internal interface IFanOverrideCoordinator
{
    Task<FanOverrideExecutionResult> ApplyMaximumSafeRpmAsync(
        string model,
        FanControlCapabilityResult capability,
        CancellationToken cancellationToken);

    Task<FanOverrideRecoveryDecision> RecoverAsync(
        string currentModel,
        FanControlCapabilityResult capability,
        CancellationToken cancellationToken);
}
