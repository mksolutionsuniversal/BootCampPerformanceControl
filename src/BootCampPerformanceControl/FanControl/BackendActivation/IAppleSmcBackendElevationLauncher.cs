namespace BootCampPerformanceControl.FanControl.BackendActivation;

public interface IAppleSmcBackendElevationLauncher
{
    Task<AppleSmcBackendElevationResult> LaunchAsync(
        CancellationToken cancellationToken);
}

public enum AppleSmcBackendElevationOutcome
{
    Completed,
    UserCanceled,
    Failed
}

public sealed record AppleSmcBackendElevationResult(
    AppleSmcBackendElevationOutcome Outcome,
    AppleSmcBackendActivationOutcome? HelperOutcome,
    int? ExitCode,
    Exception? Exception);
