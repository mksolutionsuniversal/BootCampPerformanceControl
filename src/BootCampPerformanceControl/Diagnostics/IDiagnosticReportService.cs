namespace BootCampPerformanceControl.Diagnostics;

public interface IDiagnosticReportService
{
    Task<DiagnosticReportResult> GenerateAsync(CancellationToken cancellationToken);
}
