using BootCampPerformanceControl.Diagnostics;
using BootCampPerformanceControl.ApplicationSettings;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.BackendActivation;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.UI;

internal sealed record MainApplicationComposition(
    MainViewModel ViewModel,
    CleanExitFanRecoveryService CleanExitFanRecoveryService,
    IApplicationLogger Logger);

public static class AppCompositionRoot
{
    public static MainViewModel CreateMainViewModel(IApplicationLogger logger)
    {
        return CreateMainApplication(logger).ViewModel;
    }

    internal static MainApplicationComposition CreateMainApplication(IApplicationLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var restoreSnapshotStore = new JsonRestoreSnapshotStore(logger);
        var ownershipStore = new JsonFanOverrideOwnershipStore(logger);
        var modelSupportRegistry = new ModelSupportRegistry();
        var hardwareDetectionService = new HardwareDetectionService(modelSupportRegistry);
        var profileCatalog = new ProfileCatalog();
        var profileExecutionResolver = new ProfileExecutionResolver();
        var fanProfileExecutionResolver = new FanProfileExecutionResolver();
        var fanExecutionSessionFactory = new CrystalIdeaFanExecutionSessionFactory(
            ownershipStore,
            logger);
        var processorProfileStateEvaluator = new ProcessorProfileStateEvaluator(
            profileCatalog,
            profileExecutionResolver);
        var powerManagementService = new WindowsPowerManagementService(
            restoreSnapshotStore,
            logger);
        var gamingOptimisedApplyCoordinator = new GamingOptimisedApplyCoordinator(
            profileExecutionResolver,
            fanProfileExecutionResolver,
            powerManagementService,
            fanExecutionSessionFactory);
        var gamingOptimisedRestoreCoordinator = new GamingOptimisedRestoreCoordinator(
            powerManagementService,
            fanExecutionSessionFactory);
        var gamingOptimisedFanResumeService = new GamingOptimisedFanResumeService(
            hardwareDetectionService,
            profileCatalog,
            profileExecutionResolver,
            fanProfileExecutionResolver,
            powerManagementService,
            restoreSnapshotStore,
            fanExecutionSessionFactory);
        var cleanExitFanRecoveryService = new CleanExitFanRecoveryService(
            hardwareDetectionService,
            ownershipStore,
            gamingOptimisedRestoreCoordinator,
            logger);
        var profileApplyService = new ProfileApplyService(
            hardwareDetectionService,
            profileCatalog,
            profileExecutionResolver,
            powerManagementService,
            gamingOptimisedApplyCoordinator);
        var profileRestoreService = new ProfileRestoreService(
            hardwareDetectionService,
            powerManagementService,
            gamingOptimisedRestoreCoordinator,
            restoreSnapshotStore,
            ownershipStore,
            logger);
        var diagnosticReportService = new DiagnosticReportService(
            hardwareDetectionService,
            powerManagementService,
            restoreSnapshotStore,
            profileCatalog,
            profileExecutionResolver,
            logger);
        var compatibilityReportService = new CompatibilityReportService(
            hardwareDetectionService,
            powerManagementService,
            restoreSnapshotStore,
            profileCatalog,
            profileExecutionResolver,
            logger);

        var viewModel = new MainViewModel(
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
            compatibilityReportService,
            new WpfCompatibilityReportDialogService(logger),
            logger,
            new WpfUserConfirmationService(),
            profileRestoreService: profileRestoreService,
            ownershipReader: ownershipStore,
            gamingOptimisedRestoreCoordinator: gamingOptimisedRestoreCoordinator,
            gamingOptimisedFanResumeService: gamingOptimisedFanResumeService);

        return new MainApplicationComposition(
            viewModel,
            cleanExitFanRecoveryService,
            logger);
    }
}
