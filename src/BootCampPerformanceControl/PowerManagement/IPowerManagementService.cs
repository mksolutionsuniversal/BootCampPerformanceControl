namespace BootCampPerformanceControl.PowerManagement;

public interface IPowerManagementService
{
    Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken);

    Task<PowerOperationResult> ApplyProcessorSettingsAsync(
        ProcessorPowerSettings requestedSettings,
        CancellationToken cancellationToken);

    Task<PowerOperationResult> RestoreOriginalSettingsAsync(CancellationToken cancellationToken);
}
