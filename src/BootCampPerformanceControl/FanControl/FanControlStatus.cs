namespace BootCampPerformanceControl.FanControl;

public sealed record FanControlStatus(
    bool IsAvailable,
    string DisplayText);
