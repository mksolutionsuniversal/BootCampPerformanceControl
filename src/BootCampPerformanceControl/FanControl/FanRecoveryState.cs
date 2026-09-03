namespace BootCampPerformanceControl.FanControl;

internal enum FanRecoveryState
{
    None,
    CurrentSessionOverrideActive,
    PreviousSessionRecoveryPending,
    RecoveryBlocked,
    InspectionFailed
}
