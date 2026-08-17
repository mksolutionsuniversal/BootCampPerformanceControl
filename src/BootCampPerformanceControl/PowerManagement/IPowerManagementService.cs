namespace BootCampPerformanceControl.PowerManagement;

public interface IPowerManagementService
{
    Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken);
}
