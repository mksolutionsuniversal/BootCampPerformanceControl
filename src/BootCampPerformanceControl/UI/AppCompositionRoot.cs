using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.UI;

public static class AppCompositionRoot
{
    public static MainViewModel CreateMainViewModel(IApplicationLogger logger)
    {
        return new MainViewModel(
            new HardwareDetectionService(),
            new WindowsPowerManagementService(),
            new InMemoryRestoreSnapshotStore(),
            new UnavailableFanControlService(),
            new ProfileCatalog(),
            logger);
    }
}
