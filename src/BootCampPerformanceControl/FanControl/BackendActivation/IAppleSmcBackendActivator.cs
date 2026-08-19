namespace BootCampPerformanceControl.FanControl.BackendActivation;

public interface IAppleSmcBackendActivator
{
    Task<AppleSmcBackendActivationResult> StartAsync(
        CancellationToken cancellationToken);
}
