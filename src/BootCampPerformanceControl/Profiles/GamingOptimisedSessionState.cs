namespace BootCampPerformanceControl.Profiles;

internal enum GamingOptimisedSessionState
{
    Unknown,
    NoActiveSession,
    Full,
    PartialCpuOnly,
    FanRecoveryPendingOrUnsafe,
    Other
}
