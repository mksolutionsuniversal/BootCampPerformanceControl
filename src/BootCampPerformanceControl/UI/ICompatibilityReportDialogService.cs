using BootCampPerformanceControl.Diagnostics;

namespace BootCampPerformanceControl.UI;

public interface ICompatibilityReportDialogService
{
    void Show(CompatibilityReportResult report);
}
