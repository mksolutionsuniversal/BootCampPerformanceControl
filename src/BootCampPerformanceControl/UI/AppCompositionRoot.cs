using BootCampPerformanceControl.Diagnostics;
using BootCampPerformanceControl.ApplicationSettings;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.BackendActivation;
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
        var modelSupportRegistry = new ModelSupportRegistry();
        var hardwareDetectionService = new HardwareDetectionService(modelSupportRegistry);
        var profileCatalog = new ProfileCatalog();
        var profileExecutionResolver = new ProfileExecutionResolver();
        var processorProfileStateEvaluator = new ProcessorProfileStateEvaluator(
            profileCatalog,
            profileExecutionResolver);
        var powerManagementService = new WindowsPowerManagementService(
            restoreSnapshotStore,
            logger);
        var profileApplyService = new ProfileApplyService(
            hardwareDetectionService,
            profileCatalog,
            profileExecutionResolver,
            powerManagementService);
        var diagnosticReportService = new DiagnosticReportService(
            hardwareDetectionService,
            powerManagementService,
            restoreSnapshotStore,
            profileCatalog,
            profileExecutionResolver,
            logger);

        return new MainViewModel(
            hardwareDetectionService,
            powerManagementService,
            new AppleSmcReadOnlyFanControlService(),
            new WindowsAppleSmcBackendElevationLauncher(),
            new WindowsApplicationOptionsService(),
            profileCatalog,
            profileApplyService,
            restoreSnapshotStore,
            processorProfileStateEvaluator,
            diagnosticReportService,
            new WpfDiagnosticReportFileSaveService(),
            logger,
            new WpfUserConfirmationService());
    }
}
