namespace BootCampPerformanceControl.FanControl;

internal sealed record FanOverrideRecoveryDecision(
    FanOverrideRecoveryAction Action,
    string Reason);
