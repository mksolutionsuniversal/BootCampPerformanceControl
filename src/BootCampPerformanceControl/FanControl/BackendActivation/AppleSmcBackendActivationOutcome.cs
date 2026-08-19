namespace BootCampPerformanceControl.FanControl.BackendActivation;

public enum AppleSmcBackendActivationOutcome
{
    Running,
    UnsupportedModel,
    BackendNotInstalled,
    Transitional,
    AccessDenied,
    Timeout,
    Failed
}
