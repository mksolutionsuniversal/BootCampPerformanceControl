namespace BootCampPerformanceControl.FanControl;

internal sealed record FanControllerReadResult(
    FanControlStatus Status,
    FanControlCapabilityResult Capability);
