namespace BootCampPerformanceControl.FanControl.BackendActivation;

public sealed record AppleSmcBackendActivationResult(
    AppleSmcBackendActivationOutcome Outcome,
    string Details,
    Exception? Exception = null);
