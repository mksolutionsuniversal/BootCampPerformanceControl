using BootCampPerformanceControl.Diagnostics;

namespace BootCampPerformanceControl.UI;

public interface IDiagnosticReportFileSaveService
{
    Task<bool> SaveAsync(
        DiagnosticReportResult report,
        CancellationToken cancellationToken);
}
