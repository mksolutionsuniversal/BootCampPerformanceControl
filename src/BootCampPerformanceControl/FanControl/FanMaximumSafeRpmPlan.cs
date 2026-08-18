namespace BootCampPerformanceControl.FanControl;

internal sealed record FanMaximumSafeRpmPlan(
    string Model,
    float Fan0TargetRpm,
    float Fan1TargetRpm);
