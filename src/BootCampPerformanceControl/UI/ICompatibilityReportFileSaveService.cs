using BootCampPerformanceControl.Diagnostics;

namespace BootCampPerformanceControl.UI;

internal interface ICompatibilityReportFileSaveService
{
    Task<bool> SaveAsync(
        CompatibilityReportResult report,
        CancellationToken cancellationToken);
}
