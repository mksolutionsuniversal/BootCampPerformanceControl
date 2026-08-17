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
        var restoreSnapshotStore = new JsonRestoreSnapshotStore(logger);
        var powerManagementService = new WindowsPowerManagementService(
            restoreSnapshotStore,
            logger);

        return new MainViewModel(
            new HardwareDetectionService(),
            powerManagementService,
            new UnavailableFanControlService(),
            new ProfileCatalog(),
            logger);
    }
}
