using BootCampPerformanceControl.FanControl;

namespace BootCampPerformanceControl.Diagnostics;

public interface ICompatibilityReportService
{
    Task<CompatibilityReportResult> GenerateAsync(
        FanControlStatus currentFanStatus,
        CancellationToken cancellationToken);
}
