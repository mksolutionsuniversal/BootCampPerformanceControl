namespace BootCampPerformanceControl.FanControl;

internal sealed record FanOverridePreparationResult(
    bool IsAllowed,
    FanMaximumSafeRpmPlan? Plan,
    string? FailureReason)
{
    public static FanOverridePreparationResult Allowed(FanMaximumSafeRpmPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new FanOverridePreparationResult(true, plan, null);
    }

    public static FanOverridePreparationResult Blocked(string failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        return new FanOverridePreparationResult(false, null, failureReason);
    }
}
