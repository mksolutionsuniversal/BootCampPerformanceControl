namespace BootCampPerformanceControl.FanControl;

internal sealed record FanOverrideOwnershipMarker(
    string Model,
    float Fan0ExpectedTargetRpm,
    float Fan1ExpectedTargetRpm,
    DateTimeOffset CreatedAtUtc)
{
    public static FanOverrideOwnershipMarker FromPlan(
        FanMaximumSafeRpmPlan plan,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new FanOverrideOwnershipMarker(
            plan.Model,
            plan.Fan0TargetRpm,
            plan.Fan1TargetRpm,
            createdAtUtc);
    }
}
