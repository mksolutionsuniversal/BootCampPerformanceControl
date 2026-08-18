using System.Reflection;
using BootCampPerformanceControl.Diagnostics;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;
using BootCampPerformanceControl.Tests.TestDoubles;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl.Tests.UI;

public sealed class MainViewModelTests
{
    [Fact]
    public void MainViewModel_DoesNotDependOnConcreteRestoreSnapshotStore()
    {
        var constructorParameters = typeof(MainViewModel)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters());
        var instanceFields = typeof(MainViewModel).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(
            constructorParameters,
            parameter => typeof(JsonRestoreSnapshotStore).IsAssignableFrom(parameter.ParameterType));
        Assert.DoesNotContain(
            instanceFields,
            field => typeof(JsonRestoreSnapshotStore).IsAssignableFrom(field.FieldType));
    }

    [Fact]
    public void ApplicationVersion_MatchesAssemblyInformationalVersion()
    {
        var expectedVersion = typeof(MainViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()));

        Assert.False(string.IsNullOrWhiteSpace(expectedVersion));
        Assert.Equal(expectedVersion, viewModel.ApplicationVersion);
    }

    [Fact]
    public void DetectedProfileState_InitialStateIsUnknown()
    {
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()));

        Assert.Equal(
            "Unknown - power state has not been read.",
            viewModel.DetectedProfileState);
    }

    [Fact]
    public void RestoreSnapshotStatus_InitialStateReflectsUnavailableSnapshot()
    {
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()));

        Assert.Equal("Not available.", viewModel.RestoreSnapshotStatus);
    }

    [Fact]
    public async Task RestoreSnapshotStatus_InitialStateReflectsAvailableSnapshot()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            InitialPowerState(),
            CancellationToken.None);

        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            restoreSnapshotStore);

        Assert.Equal(
            "Available - original processor settings can be restored.",
            viewModel.RestoreSnapshotStatus);
    }

    [Fact]
    public async Task Refresh_WithVerifiedExactGamingPowerState_DisplaysGamingOptimisedDetected()
    {
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(GamingOptimisedPowerState()));

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal("Gaming Optimised settings detected.", viewModel.DetectedProfileState);
    }

    [Fact]
    public async Task Refresh_WithDifferingPowerState_DisplaysWindowsCustomProcessorSettings()
    {
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()));

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal("Windows / custom processor settings.", viewModel.DetectedProfileState);
    }

    [Fact]
    public async Task Refresh_WithPowerReadFailure_ClearsPreviouslyDetectedGamingStateToUnknown()
    {
        var powerManagementService = new FakePowerManagementService(GamingOptimisedPowerState());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        Assert.Equal("Gaming Optimised settings detected.", viewModel.DetectedProfileState);

        powerManagementService.QueueReadException(new InvalidOperationException("Power read failed."));

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(
            "Unknown - power state has not been read.",
            viewModel.DetectedProfileState);
    }

    [Fact]
    public void ExportDiagnosticReportCommand_IsExposed()
    {
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()));

        Assert.NotNull(viewModel.ExportDiagnosticReportCommand);
        Assert.True(viewModel.ExportDiagnosticReportCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExportDiagnosticReportCommand_SuccessfulExportGeneratesAndSavesReportOnce()
    {
        var diagnosticReportService = new FakeDiagnosticReportService();
        var diagnosticReportFileSaveService = new FakeDiagnosticReportFileSaveService();
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            diagnosticReportService: diagnosticReportService,
            diagnosticReportFileSaveService: diagnosticReportFileSaveService);

        viewModel.ExportDiagnosticReportCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, diagnosticReportService.GenerateCallCount);
        Assert.Equal(1, diagnosticReportFileSaveService.SaveCallCount);
        Assert.Equal(diagnosticReportService.Report, diagnosticReportFileSaveService.LastReport);
        Assert.Equal("Diagnostic report exported successfully.", viewModel.StatusMessage);
        Assert.Equal(0, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Equal(0, powerManagementService.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task ExportDiagnosticReportCommand_UserCancellationReportsCanceledWithoutError()
    {
        var diagnosticReportService = new FakeDiagnosticReportService();
        var diagnosticReportFileSaveService = new FakeDiagnosticReportFileSaveService
        {
            SaveResult = false
        };
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            logger: logger,
            diagnosticReportService: diagnosticReportService,
            diagnosticReportFileSaveService: diagnosticReportFileSaveService);

        viewModel.ExportDiagnosticReportCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, diagnosticReportService.GenerateCallCount);
        Assert.Equal(1, diagnosticReportFileSaveService.SaveCallCount);
        Assert.Equal("Diagnostic report export canceled.", viewModel.StatusMessage);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task ExportDiagnosticReportCommand_SaveFailureDoesNotLogSelectedFilePath()
    {
        const string privatePath = @"C:\Users\Alice\Desktop\diagnostics.txt";
        var diagnosticReportFileSaveService = new FakeDiagnosticReportFileSaveService
        {
            SaveException = new IOException($"Access to '{privatePath}' was denied.")
        };
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            logger: logger,
            diagnosticReportFileSaveService: diagnosticReportFileSaveService);

        viewModel.ExportDiagnosticReportCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal("Diagnostic report export failed. Check the log for details.", viewModel.StatusMessage);
        var error = Assert.Single(logger.Errors);
        Assert.DoesNotContain(privatePath, error.Message);
        Assert.DoesNotContain(privatePath, error.Exception.ToString());
    }

    [Fact]
    public async Task ExportDiagnosticReportCommand_CannotExecuteWhileRefreshIsBusy()
    {
        var hardwareDetectionService = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var diagnosticReportService = new FakeDiagnosticReportService();
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            powerManagementService,
            diagnosticReportService: diagnosticReportService);
        var refreshDetectGate = new AsyncGate();
        hardwareDetectionService.QueueDetectGate(refreshDetectGate);

        viewModel.RefreshCommand.Execute(null);
        await refreshDetectGate.WaitUntilEnteredAsync();

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.ExportDiagnosticReportCommand.CanExecute(null));

        viewModel.ExportDiagnosticReportCommand.Execute(null);

        Assert.Equal(0, diagnosticReportService.GenerateCallCount);

        refreshDetectGate.Release();
        await WaitForIdleAsync(viewModel);
    }

    [Fact]
    public async Task ExportDiagnosticReportCommand_DisablesOtherOperationCommandsWhileBusy()
    {
        var diagnosticReportService = new FakeDiagnosticReportService();
        var diagnosticReportFileSaveService = new FakeDiagnosticReportFileSaveService();
        var diagnosticGenerateGate = new AsyncGate();
        diagnosticReportService.QueueGenerateGate(diagnosticGenerateGate);
        var hardwareDetectionService = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            powerManagementService,
            diagnosticReportService: diagnosticReportService,
            diagnosticReportFileSaveService: diagnosticReportFileSaveService);

        viewModel.RefreshCommand.Execute(null);
        var gamingCommand = GetProfile(viewModel, "gaming-optimised").Command!;
        var detectCallCountAfterRefresh = hardwareDetectionService.DetectCallCount;

        viewModel.ExportDiagnosticReportCommand.Execute(null);
        await diagnosticGenerateGate.WaitUntilEnteredAsync();

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.ExportDiagnosticReportCommand.CanExecute(null));
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(gamingCommand.CanExecute(null));

        viewModel.RefreshCommand.Execute(null);
        gamingCommand.Execute(null);

        Assert.Equal(detectCallCountAfterRefresh, hardwareDetectionService.DetectCallCount);
        Assert.Equal(0, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);

        diagnosticGenerateGate.Release();
        await WaitForIdleAsync(viewModel);
    }

    [Fact]
    public void ProfileButtons_GamingOptimisedIsEnabledForVerifiedMacBookPro16_1()
    {
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()));

        viewModel.RefreshCommand.Execute(null);

        var gaming = GetProfile(viewModel, "gaming-optimised");
        Assert.Equal("gaming-optimised", gaming.ProfileId);
        Assert.True(gaming.IsEnabled);
        Assert.NotNull(gaming.Command);
        Assert.Contains("Apply", gaming.ToolTip);
    }

    [Fact]
    public void ProfileButtons_GamingOptimisedIsDisabledForUnsupportedHardware()
    {
        var unsupportedResult = new ModelVerificationResult(
            "PC Manufacturer",
            "PC Model",
            PlatformSupportStatus.UnsupportedNonApple,
            ModelValidationLevel.NotIndividuallyTested,
            "Not Apple hardware.");
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(unsupportedResult),
            new FakePowerManagementService(InitialPowerState()));

        viewModel.RefreshCommand.Execute(null);

        var gaming = GetProfile(viewModel, "gaming-optimised");
        Assert.False(gaming.IsEnabled);
        Assert.Null(gaming.Command);
        Assert.Equal("Gaming Optimised is available for supported Intel Mac models.", gaming.ToolTip);
    }

    [Fact]
    public void ProfileButtons_ContainsOnlyGamingOptimisedAndRestore()
    {
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()));

        viewModel.RefreshCommand.Execute(null);

        Assert.Equal(2, viewModel.ProfileButtons.Count);
        Assert.Contains(viewModel.ProfileButtons, profile => profile.ProfileId == "gaming-optimised");
        Assert.Contains(viewModel.ProfileButtons, profile => profile.ProfileId == "restore");
        Assert.DoesNotContain(viewModel.ProfileButtons, profile => profile.ProfileId == "balanced");
        Assert.DoesNotContain(viewModel.ProfileButtons, profile => profile.ProfileId == "full-performance");
    }

    [Fact]
    public void ProfileButtons_RestoreIsDisabledWhenNoOriginalSnapshotExists()
    {
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()));

        var restore = GetProfile(viewModel, "restore");
        Assert.False(restore.IsEnabled);
        Assert.Null(restore.Command);
        Assert.Contains("No original restore snapshot", restore.ToolTip, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Not available.", viewModel.RestoreSnapshotStatus);
    }

    [Fact]
    public async Task ProfileButtons_RestoreIsEnabledWhenOriginalSnapshotExists()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            InitialPowerState(),
            CancellationToken.None);
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            restoreSnapshotStore);

        var restore = GetProfile(viewModel, "restore");
        Assert.True(restore.IsEnabled);
        Assert.NotNull(restore.Command);
        Assert.Contains("exact original saved power state", restore.ToolTip, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "Available - original processor settings can be restored.",
            viewModel.RestoreSnapshotStatus);
    }

    [Fact]
    public void GamingButton_ReverifiesThroughProfileApplyServiceBeforeAnyWrite()
    {
        var unsupportedSecondResult = new ModelVerificationResult(
            "PC Manufacturer",
            "PC Model",
            PlatformSupportStatus.UnsupportedNonApple,
            ModelValidationLevel.NotIndividuallyTested,
            "Hardware changed to unsupported.");
        var hardwareDetectionService = new FakeHardwareDetectionService(
            VerifiedMacBookPro16_1(),
            unsupportedSecondResult);
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            powerManagementService,
            logger: logger);

        viewModel.RefreshCommand.Execute(null);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);

        Assert.Equal(2, hardwareDetectionService.DetectCallCount);
        Assert.Equal(2, hardwareDetectionService.VerifyModelCallCount);
        Assert.Equal(1, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal(0, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Contains("Profile application failed:", viewModel.StatusMessage);
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task GamingButton_FailedApplyAfterRestoreSnapshotSavedRefreshesRestoreUiAndClearsDetectedProfileState()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var expectedStateBefore = GamingOptimisedPowerState();
        var requestedSettings = new ProcessorPowerSettings(95, 95, 0, 0);
        var powerManagementService = new FakePowerManagementService(
            FailedPowerOperation(expectedStateBefore, requestedSettings, "Native write failed."),
            GamingOptimisedPowerState(),
            expectedStateBefore)
        {
            SaveRestoreSnapshotBeforeGuardedApplyResult = true
        };
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        Assert.Equal("Gaming Optimised settings detected.", viewModel.DetectedProfileState);
        Assert.Equal("Not available.", viewModel.RestoreSnapshotStatus);
        Assert.False(GetProfile(viewModel, "restore").IsEnabled);

        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.True(restoreSnapshotStore.HasOriginalRestoreSnapshot);
        Assert.Equal(
            "Available - original processor settings can be restored.",
            viewModel.RestoreSnapshotStatus);
        Assert.True(GetProfile(viewModel, "restore").IsEnabled);
        Assert.NotNull(GetProfile(viewModel, "restore").Command);
        Assert.Equal(
            "Unknown - power state has not been read.",
            viewModel.DetectedProfileState);
    }

    [Fact]
    public async Task GamingButton_CanceledApplyAfterRestoreSnapshotSavedRefreshesRestoreUiAndClearsDetectedProfileState()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var expectedStateBefore = GamingOptimisedPowerState();
        var requestedSettings = new ProcessorPowerSettings(95, 95, 0, 0);
        var powerManagementService = new FakePowerManagementService(
            FailedPowerOperation(expectedStateBefore, requestedSettings, "Apply canceled."),
            GamingOptimisedPowerState(),
            expectedStateBefore)
        {
            SaveRestoreSnapshotBeforeGuardedApplyResult = true,
            GuardedApplyException = new OperationCanceledException("Apply canceled after snapshot save.")
        };
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        Assert.Equal("Gaming Optimised settings detected.", viewModel.DetectedProfileState);
        Assert.Equal("Not available.", viewModel.RestoreSnapshotStatus);
        Assert.False(GetProfile(viewModel, "restore").IsEnabled);

        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.True(restoreSnapshotStore.HasOriginalRestoreSnapshot);
        Assert.Equal(
            "Available - original processor settings can be restored.",
            viewModel.RestoreSnapshotStatus);
        Assert.True(GetProfile(viewModel, "restore").IsEnabled);
        Assert.NotNull(GetProfile(viewModel, "restore").Command);
        Assert.Equal(
            "Unknown - power state has not been read.",
            viewModel.DetectedProfileState);
        Assert.Equal("Profile application canceled.", viewModel.StatusMessage);
    }

    [Fact]
    public void GamingButton_SuccessfulApplyReReadsPowerStateAndUpdatesDisplayedValues()
    {
        var expectedStateBefore = PowerState(
            ProcessorMaximumAc: 80,
            ProcessorMaximumDc: 70,
            BoostModeAc: 2,
            BoostModeDc: 2);
        var refreshedStateAfterApply = PowerState(
            ProcessorMaximumAc: 95,
            ProcessorMaximumDc: 95,
            BoostModeAc: 0,
            BoostModeDc: 0);
        var requestedSettings = new ProcessorPowerSettings(95, 95, 0, 0);
        var powerManagementService = new FakePowerManagementService(
            SuccessfulPowerOperation(expectedStateBefore, requestedSettings),
            InitialPowerState(),
            expectedStateBefore,
            refreshedStateAfterApply);
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService);

        viewModel.RefreshCommand.Execute(null);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);

        Assert.Equal(3, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal(1, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Equal(requestedSettings, powerManagementService.LastGuardedSettings);
        Assert.Equal(expectedStateBefore, powerManagementService.LastExpectedStateBefore);
        Assert.Equal("95%", viewModel.ProcessorMaximumAc);
        Assert.Equal("95%", viewModel.ProcessorMaximumDc);
        Assert.Equal("0 (Disabled)", viewModel.BoostModeAc);
        Assert.Equal("0 (Disabled)", viewModel.BoostModeDc);
        Assert.Contains("applied successfully", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public void Compatibility_DisplaysPlatformSupportAndModelValidationIndependently()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.PerformanceValidated,
            "Performance validated model.");
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(verification),
            new FakePowerManagementService(InitialPowerState()));

        viewModel.RefreshCommand.Execute(null);

        Assert.Equal("Supported Intel Mac", viewModel.PlatformSupport);
        Assert.Equal("Performance validated", viewModel.ModelValidation);
        Assert.Equal("Performance validated model.", viewModel.CompatibilityDetails);
    }

    [Fact]
    public void Compatibility_DisplaysNotIndividuallyTestedIntelMac()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro14_3,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.NotIndividuallyTested,
            "Supported Intel Mac.");
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(verification),
            new FakePowerManagementService(InitialPowerState()));

        viewModel.RefreshCommand.Execute(null);

        Assert.Equal("Supported Intel Mac", viewModel.PlatformSupport);
        Assert.Equal("Not individually tested", viewModel.ModelValidation);
    }

    [Fact]
    public void Compatibility_DisplaysUnsupportedNonApple()
    {
        var verification = new ModelVerificationResult(
            "PC Manufacturer",
            "PC Model",
            PlatformSupportStatus.UnsupportedNonApple,
            ModelValidationLevel.NotIndividuallyTested,
            "Not Apple.");
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(verification),
            new FakePowerManagementService(InitialPowerState()));

        viewModel.RefreshCommand.Execute(null);

        Assert.Equal("Unsupported - non-Apple hardware", viewModel.PlatformSupport);
        Assert.Equal("Not individually tested", viewModel.ModelValidation);
    }

    [Fact]
    public async Task ProfileButtons_GamingOptimisedDisabledWhenPowerReadFails()
    {
        var powerManagementService = new FakePowerManagementService();
        powerManagementService.QueueReadException(new InvalidOperationException("Power read failed."));
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var gaming = GetProfile(viewModel, "gaming-optimised");
        Assert.False(gaming.IsEnabled);
        Assert.Null(gaming.Command);
        Assert.Contains("requires current processor power settings to be read successfully", gaming.ToolTip);
    }

    [Fact]
    public void GamingButton_NotIndividuallyTested_ShowsConfirmationDialog_CancelAbortsApplyWithoutWrites()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro14_3,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.NotIndividuallyTested,
            "Not individually tested.");
        var confirmationService = new FakeUserConfirmationService { Result = false };
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(verification),
            powerManagementService,
            logger: logger,
            userConfirmationService: confirmationService);

        viewModel.RefreshCommand.Execute(null);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);

        Assert.Equal(1, confirmationService.CallCount);
        Assert.Equal(VerifiedHardwareModels.MacBookPro14_3, confirmationService.LastModelName);
        Assert.Equal("Profile application canceled.", viewModel.StatusMessage);
        Assert.Equal(0, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
    }

    [Fact]
    public async Task GamingButton_NotIndividuallyTested_ShowsConfirmationDialog_ConfirmAllowsApplyAndRemembersSession()
    {
        var verification = new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro14_3,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.NotIndividuallyTested,
            "Not individually tested.");
        var confirmationService = new FakeUserConfirmationService { Result = true };
        var expectedStateBefore = InitialPowerState();
        var requestedSettings = new ProcessorPowerSettings(95, 95, 0, 0);
        var refreshedState = GamingOptimisedPowerState();
        var powerManagementService = new FakePowerManagementService(
            SuccessfulPowerOperation(expectedStateBefore, requestedSettings),
            InitialPowerState(),
            expectedStateBefore,
            refreshedState);
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(verification),
            powerManagementService,
            userConfirmationService: confirmationService);

        viewModel.RefreshCommand.Execute(null);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, confirmationService.CallCount);
        Assert.Equal(1, powerManagementService.GuardedApplyCallCount);
        Assert.Contains("applied successfully", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        // Second apply in same session should NOT prompt again
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, confirmationService.CallCount);
        Assert.Equal(2, powerManagementService.GuardedApplyCallCount);
    }

    [Fact]
    public async Task GamingButton_PerformanceValidated_DoesNotShowConfirmationDialog()
    {
        var confirmationService = new FakeUserConfirmationService { Result = true };
        var expectedStateBefore = InitialPowerState();
        var requestedSettings = new ProcessorPowerSettings(95, 95, 0, 0);
        var refreshedState = GamingOptimisedPowerState();
        var powerManagementService = new FakePowerManagementService(
            SuccessfulPowerOperation(expectedStateBefore, requestedSettings),
            InitialPowerState(),
            expectedStateBefore,
            refreshedState);
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            userConfirmationService: confirmationService);

        viewModel.RefreshCommand.Execute(null);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(0, confirmationService.CallCount);
        Assert.Equal(1, powerManagementService.GuardedApplyCallCount);
    }

    [Fact]
    public async Task GamingButton_SuccessfulApplyMakesRestoreAvailable()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var expectedStateBefore = InitialPowerState();
        var requestedSettings = new ProcessorPowerSettings(95, 95, 0, 0);
        var refreshedStateAfterApply = PowerState(
            ProcessorMaximumAc: 95,
            ProcessorMaximumDc: 95,
            BoostModeAc: 0,
            BoostModeDc: 0);
        var powerManagementService = new FakePowerManagementService(
            SuccessfulPowerOperation(expectedStateBefore, requestedSettings),
            InitialPowerState(),
            expectedStateBefore,
            refreshedStateAfterApply);
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore);

        Assert.False(GetProfile(viewModel, "restore").IsEnabled);

        viewModel.RefreshCommand.Execute(null);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);

        Assert.True(restoreSnapshotStore.HasOriginalRestoreSnapshot);
        var restore = GetProfile(viewModel, "restore");
        Assert.True(restore.IsEnabled);
        Assert.NotNull(restore.Command);
        await WaitForIdleAsync(viewModel);
    }

    [Fact]
    public void GamingButton_SuccessfulApplyUiRereadExceptionReportsRefreshFailureNotApplyFailure()
    {
        var expectedStateBefore = InitialPowerState();
        var requestedSettings = new ProcessorPowerSettings(95, 95, 0, 0);
        var powerManagementService = new FakePowerManagementService(
            SuccessfulPowerOperation(expectedStateBefore, requestedSettings),
            InitialPowerState(),
            expectedStateBefore);
        powerManagementService.QueueReadException(new InvalidOperationException("UI read failed."));
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            logger: logger);

        viewModel.RefreshCommand.Execute(null);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);

        Assert.Contains("was applied and verified", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refreshing the displayed power state failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Profile application failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal(1, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Equal(0, powerManagementService.RestoreOriginalSettingsCallCount);
        Assert.Single(logger.Errors);
        Assert.Contains("UI refresh failed", logger.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GamingButton_SuccessfulApplyUiRereadCancellationReportsApplySuccessWithCanceledRefresh()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var expectedStateBefore = InitialPowerState();
        var requestedSettings = new ProcessorPowerSettings(95, 95, 0, 0);
        var powerManagementService = new FakePowerManagementService(
            SuccessfulPowerOperation(expectedStateBefore, requestedSettings),
            InitialPowerState(),
            expectedStateBefore);
        powerManagementService.QueueReadException(new OperationCanceledException("UI refresh canceled."));
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore,
            logger: logger);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(
            "Profile 'Gaming Optimised' was applied and verified, but refreshing the displayed power state was canceled. Use Refresh to update the display.",
            viewModel.StatusMessage);
        Assert.DoesNotContain("Profile application canceled", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed. Check the log", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "Unknown - power state has not been read.",
            viewModel.DetectedProfileState);
        Assert.True(restoreSnapshotStore.HasOriginalRestoreSnapshot);
        Assert.Equal(
            "Available - original processor settings can be restored.",
            viewModel.RestoreSnapshotStatus);
        Assert.True(GetProfile(viewModel, "restore").IsEnabled);
        Assert.Equal(3, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal(1, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Equal(0, powerManagementService.RestoreOriginalSettingsCallCount);
        Assert.Empty(logger.Errors);
        Assert.Contains(
            logger.InformationMessages,
            message => message.Contains("UI refresh canceled", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            logger.InformationMessages,
            message => message.StartsWith("Profile application canceled:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RestoreButton_InvokesRestoreBackendExactlyOnce()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var originalSnapshot = PowerState(
            ProcessorMaximumAc: 100,
            ProcessorMaximumDc: 90,
            BoostModeAc: 2,
            BoostModeDc: 2);
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);
        var powerManagementService = new FakePowerManagementService(originalSnapshot);
        powerManagementService.RestoreResult = SuccessfulRestoreOperation(
            InitialPowerState(),
            ProcessorPowerSettings.FromSnapshot(originalSnapshot));
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore);

        GetProfile(viewModel, "restore").Command!.Execute(null);

        Assert.Equal(1, powerManagementService.RestoreOriginalSettingsCallCount);
        Assert.Equal(0, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
    }

    [Fact]
    public async Task RestoreButton_SuccessfulRestoreReReadsPowerStateUpdatesDisplayAndDisablesRestore()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var originalSnapshot = PowerState(
            ProcessorMaximumAc: 100,
            ProcessorMaximumDc: 90,
            BoostModeAc: 1,
            BoostModeDc: 2);
        var refreshedStateAfterRestore = originalSnapshot;
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);
        var powerManagementService = new FakePowerManagementService(refreshedStateAfterRestore);
        powerManagementService.RestoreResult = SuccessfulRestoreOperation(
            InitialPowerState(),
            ProcessorPowerSettings.FromSnapshot(originalSnapshot));
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore);

        GetProfile(viewModel, "restore").Command!.Execute(null);

        Assert.Equal(1, powerManagementService.RestoreOriginalSettingsCallCount);
        Assert.Equal(1, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal("100%", viewModel.ProcessorMaximumAc);
        Assert.Equal("90%", viewModel.ProcessorMaximumDc);
        Assert.Equal("1 (Enabled)", viewModel.BoostModeAc);
        Assert.Equal("2 (Aggressive)", viewModel.BoostModeDc);
        Assert.Equal("Windows / custom processor settings.", viewModel.DetectedProfileState);
        Assert.Contains("restored successfully", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(restoreSnapshotStore.HasOriginalRestoreSnapshot);
        Assert.Equal("Not available.", viewModel.RestoreSnapshotStatus);
        Assert.False(GetProfile(viewModel, "restore").IsEnabled);
    }

    [Fact]
    public async Task RestoreButton_FailedRestoreDoesNotPretendSuccess()
    {
        const string failureReason = "Restore verification failed.";
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            InitialPowerState(),
            CancellationToken.None);
        var powerManagementService = new FakePowerManagementService(InitialPowerState())
        {
            RestoreResult = FailedRestoreOperation(failureReason)
        };
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore,
            logger);

        GetProfile(viewModel, "restore").Command!.Execute(null);

        Assert.Equal($"Restore failed: {failureReason}", viewModel.StatusMessage);
        Assert.DoesNotContain("success", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(restoreSnapshotStore.HasOriginalRestoreSnapshot);
        Assert.True(GetProfile(viewModel, "restore").IsEnabled);
        Assert.Equal(1, powerManagementService.RestoreOriginalSettingsCallCount);
        Assert.Equal(0, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task RestoreButton_BackendSuccessUiRereadFailureReportsRestoreSuccessWithRefreshFailure()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var originalSnapshot = InitialPowerState();
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);
        var powerManagementService = new FakePowerManagementService();
        powerManagementService.RestoreResult = SuccessfulRestoreOperation(
            PowerState(
                ProcessorMaximumAc: 95,
                ProcessorMaximumDc: 95,
                BoostModeAc: 0,
                BoostModeDc: 0),
            ProcessorPowerSettings.FromSnapshot(originalSnapshot));
        powerManagementService.QueueReadException(new InvalidOperationException("UI restore read failed."));
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore,
            logger);

        GetProfile(viewModel, "restore").Command!.Execute(null);

        Assert.Contains("restored and verified", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refreshing the displayed power state failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Restore failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(restoreSnapshotStore.HasOriginalRestoreSnapshot);
        Assert.False(GetProfile(viewModel, "restore").IsEnabled);
        Assert.Equal(1, powerManagementService.RestoreOriginalSettingsCallCount);
        Assert.Equal(0, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task RestoreButton_SuccessfulRestoreUiRereadCancellationReportsRestoreSuccessWithCanceledRefresh()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var originalSnapshot = InitialPowerState();
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);
        var powerManagementService = new FakePowerManagementService();
        powerManagementService.RestoreResult = SuccessfulRestoreOperation(
            PowerState(
                ProcessorMaximumAc: 95,
                ProcessorMaximumDc: 95,
                BoostModeAc: 0,
                BoostModeDc: 0),
            ProcessorPowerSettings.FromSnapshot(originalSnapshot));
        powerManagementService.QueueReadException(new OperationCanceledException("UI restore refresh canceled."));
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore,
            logger);

        GetProfile(viewModel, "restore").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(
            "Original processor settings were restored and verified, but refreshing the displayed power state was canceled. Use Refresh to update the display.",
            viewModel.StatusMessage);
        Assert.DoesNotContain("Restore canceled", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "Unknown - power state has not been read.",
            viewModel.DetectedProfileState);
        Assert.False(restoreSnapshotStore.HasOriginalRestoreSnapshot);
        Assert.Equal("Not available.", viewModel.RestoreSnapshotStatus);
        Assert.False(GetProfile(viewModel, "restore").IsEnabled);
        Assert.Equal(1, powerManagementService.RestoreOriginalSettingsCallCount);
        Assert.Equal(1, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal(0, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Empty(logger.Errors);
        Assert.Contains(
            logger.InformationMessages,
            message => message.Contains("UI refresh canceled", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            logger.InformationMessages,
            message => message.StartsWith("Restore canceled:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RestoreButton_CanceledBeforeRestoreCompletesReportsRestoreCanceled()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            InitialPowerState(),
            CancellationToken.None);
        var powerManagementService = new FakePowerManagementService
        {
            RestoreException = new OperationCanceledException("Restore canceled before completion.")
        };
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore,
            logger);

        GetProfile(viewModel, "restore").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal("Restore canceled.", viewModel.StatusMessage);
        Assert.True(restoreSnapshotStore.HasOriginalRestoreSnapshot);
        Assert.True(GetProfile(viewModel, "restore").IsEnabled);
        Assert.Equal(1, powerManagementService.RestoreOriginalSettingsCallCount);
        Assert.Equal(0, powerManagementService.ReadCurrentStateCallCount);
        Assert.Empty(logger.Errors);
        Assert.Contains(
            logger.InformationMessages,
            message => message.StartsWith("Restore canceled:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GamingButton_FailedApplyShowsFailureReasonWithoutSuccess()
    {
        const string failureReason = "Backend verification failed.";
        var expectedStateBefore = InitialPowerState();
        var powerManagementService = new FakePowerManagementService(
            FailedPowerOperation(
                expectedStateBefore,
                new ProcessorPowerSettings(95, 95, 0, 0),
                failureReason),
            InitialPowerState(),
            expectedStateBefore);
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            logger: logger);

        viewModel.RefreshCommand.Execute(null);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);

        Assert.Equal($"Profile application failed: {failureReason}", viewModel.StatusMessage);
        Assert.DoesNotContain("success", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal(1, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Single(logger.Errors);
        Assert.Contains(failureReason, logger.Errors[0].Message);
    }

    [Fact]
    public async Task RefreshCommand_CannotExecuteWhileProfileApplyIsBusy()
    {
        var hardwareDetectionService = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var viewModel = CreateViewModel(hardwareDetectionService, powerManagementService);

        viewModel.RefreshCommand.Execute(null);
        var gamingCommand = GetProfile(viewModel, "gaming-optimised").Command!;
        var applyDetectGate = new AsyncGate();
        hardwareDetectionService.QueueDetectGate(applyDetectGate);

        gamingCommand.Execute(null);
        await applyDetectGate.WaitUntilEnteredAsync();

        var detectCallCountWhileBusy = hardwareDetectionService.DetectCallCount;
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));

        viewModel.RefreshCommand.Execute(null);

        Assert.Equal(detectCallCountWhileBusy, hardwareDetectionService.DetectCallCount);

        applyDetectGate.Release();
        await WaitForIdleAsync(viewModel);
    }

    [Fact]
    public async Task GamingOptimisedCommand_CannotExecuteWhileRefreshIsBusy()
    {
        var hardwareDetectionService = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var viewModel = CreateViewModel(hardwareDetectionService, powerManagementService);

        viewModel.RefreshCommand.Execute(null);
        var gamingCommand = GetProfile(viewModel, "gaming-optimised").Command!;
        var refreshDetectGate = new AsyncGate();
        hardwareDetectionService.QueueDetectGate(refreshDetectGate);

        viewModel.RefreshCommand.Execute(null);
        await refreshDetectGate.WaitUntilEnteredAsync();

        var detectCallCountWhileBusy = hardwareDetectionService.DetectCallCount;
        Assert.True(viewModel.IsBusy);
        Assert.False(gamingCommand.CanExecute(null));

        gamingCommand.Execute(null);

        Assert.Equal(detectCallCountWhileBusy, hardwareDetectionService.DetectCallCount);

        refreshDetectGate.Release();
        await WaitForIdleAsync(viewModel);
    }

    [Fact]
    public async Task RefreshProfileRebuildWhileBusy_DoesNotCreateExecutableGamingCommand()
    {
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var powerReadGate = new AsyncGate();
        powerManagementService.QueueReadGate(powerReadGate);

        viewModel.RefreshCommand.Execute(null);
        await powerReadGate.WaitUntilEnteredAsync();

        var rebuiltGaming = GetProfile(viewModel, "gaming-optimised");
        Assert.True(viewModel.IsBusy);
        Assert.True(rebuiltGaming.IsEnabled);
        Assert.NotNull(rebuiltGaming.Command);
        Assert.False(rebuiltGaming.Command.CanExecute(null));

        powerReadGate.Release();
        await WaitForIdleAsync(viewModel);
    }

    [Fact]
    public async Task Commands_BecomeExecutableAgainAfterBusyReturnsFalse()
    {
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var powerReadGate = new AsyncGate();
        powerManagementService.QueueReadGate(powerReadGate);

        viewModel.RefreshCommand.Execute(null);
        await powerReadGate.WaitUntilEnteredAsync();
        var rebuiltGamingCommand = GetProfile(viewModel, "gaming-optimised").Command!;

        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(rebuiltGamingCommand.CanExecute(null));

        powerReadGate.Release();
        await WaitForIdleAsync(viewModel);

        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        Assert.True(rebuiltGamingCommand.CanExecute(null));
    }

    [Fact]
    public async Task RestoreCommand_CannotExecuteWhileRefreshIsBusy()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            InitialPowerState(),
            CancellationToken.None);
        var hardwareDetectionService = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            powerManagementService,
            restoreSnapshotStore);
        var restoreCommand = GetProfile(viewModel, "restore").Command!;
        var refreshDetectGate = new AsyncGate();
        hardwareDetectionService.QueueDetectGate(refreshDetectGate);

        viewModel.RefreshCommand.Execute(null);
        await refreshDetectGate.WaitUntilEnteredAsync();

        Assert.True(viewModel.IsBusy);
        Assert.False(restoreCommand.CanExecute(null));

        restoreCommand.Execute(null);

        Assert.Equal(0, powerManagementService.RestoreOriginalSettingsCallCount);

        refreshDetectGate.Release();
        await WaitForIdleAsync(viewModel);
    }

    [Fact]
    public async Task RefreshAndGamingCommands_CannotExecuteWhileRestoreIsBusy()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var originalSnapshot = InitialPowerState();
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);
        var hardwareDetectionService = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var powerManagementService = new FakePowerManagementService(
            InitialPowerState(),
            originalSnapshot);
        powerManagementService.RestoreResult = SuccessfulRestoreOperation(
            PowerState(
                ProcessorMaximumAc: 95,
                ProcessorMaximumDc: 95,
                BoostModeAc: 0,
                BoostModeDc: 0),
            ProcessorPowerSettings.FromSnapshot(originalSnapshot));
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            powerManagementService,
            restoreSnapshotStore);

        viewModel.RefreshCommand.Execute(null);
        var gamingCommand = GetProfile(viewModel, "gaming-optimised").Command!;
        var restoreCommand = GetProfile(viewModel, "restore").Command!;
        var restoreUiReadGate = new AsyncGate();
        powerManagementService.QueueReadGate(restoreUiReadGate);

        restoreCommand.Execute(null);
        await restoreUiReadGate.WaitUntilEnteredAsync();

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(gamingCommand.CanExecute(null));

        viewModel.RefreshCommand.Execute(null);
        gamingCommand.Execute(null);

        Assert.Equal(1, hardwareDetectionService.DetectCallCount);
        Assert.Equal(0, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Equal(1, powerManagementService.RestoreOriginalSettingsCallCount);

        restoreUiReadGate.Release();
        await WaitForIdleAsync(viewModel);
    }

    private static MainViewModel CreateViewModel(
        FakeHardwareDetectionService hardwareDetectionService,
        FakePowerManagementService powerManagementService,
        IRestoreSnapshotStore? restoreSnapshotStore = null,
        TestApplicationLogger? logger = null,
        FakeDiagnosticReportService? diagnosticReportService = null,
        FakeDiagnosticReportFileSaveService? diagnosticReportFileSaveService = null,
        IUserConfirmationService? userConfirmationService = null)
    {
        var profileCatalog = new ProfileCatalog();
        var profileExecutionResolver = new ProfileExecutionResolver();
        restoreSnapshotStore ??= new InMemoryRestoreSnapshotStore();
        powerManagementService.RestoreSnapshotStore = restoreSnapshotStore;

        return new MainViewModel(
            hardwareDetectionService,
            powerManagementService,
            new FakeFanControlService(),
            profileCatalog,
            new ProfileApplyService(
                hardwareDetectionService,
                profileCatalog,
                profileExecutionResolver,
                powerManagementService),
            restoreSnapshotStore,
            new ProcessorProfileStateEvaluator(
                profileCatalog,
                profileExecutionResolver),
            diagnosticReportService ?? new FakeDiagnosticReportService(),
            diagnosticReportFileSaveService ?? new FakeDiagnosticReportFileSaveService(),
            logger ?? new TestApplicationLogger(),
            userConfirmationService);
    }

    private static ProfileButtonViewModel GetProfile(MainViewModel viewModel, string profileId)
    {
        return Assert.Single(
            viewModel.ProfileButtons,
            profile => string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WaitForIdleAsync(MainViewModel viewModel)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (viewModel.IsBusy && !timeout.IsCancellationRequested)
        {
            await Task.Delay(10, timeout.Token);
        }

        Assert.False(viewModel.IsBusy);
    }

    private static ModelVerificationResult VerifiedMacBookPro16_1()
    {
        return new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.PerformanceValidated,
            "Verified.");
    }

    private static ModelVerificationResult UnverifiedMacBookPro16_1()
    {
        return new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.NotIndividuallyTested,
            "Matching model string without verification.");
    }

    private static PowerStateSnapshot InitialPowerState()
    {
        return PowerState(
            ProcessorMaximumAc: 80,
            ProcessorMaximumDc: 70,
            BoostModeAc: 2,
            BoostModeDc: 2);
    }

    private static PowerStateSnapshot GamingOptimisedPowerState()
    {
        return PowerState(
            ProcessorMaximumAc: 95,
            ProcessorMaximumDc: 95,
            BoostModeAc: 0,
            BoostModeDc: 0);
    }

    private static PowerStateSnapshot PowerState(
        uint ProcessorMaximumAc,
        uint ProcessorMaximumDc,
        uint BoostModeAc,
        uint BoostModeDc)
    {
        return new PowerStateSnapshot(
            Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"),
            ProcessorMaximumAc,
            ProcessorMaximumDc,
            BoostModeAc,
            BoostModeDc,
            DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
    }

    private static PowerOperationResult SuccessfulPowerOperation(
        PowerStateSnapshot stateBefore,
        ProcessorPowerSettings requestedSettings)
    {
        var stateAfter = stateBefore with
        {
            ProcessorMaximumAc = requestedSettings.ProcessorMaximumAc,
            ProcessorMaximumDc = requestedSettings.ProcessorMaximumDc,
            BoostModeAc = requestedSettings.BoostModeAc,
            BoostModeDc = requestedSettings.BoostModeDc
        };

        return new PowerOperationResult(
            PowerOperationKind.ApplyProcessorSettings,
            IsSuccessful: true,
            stateBefore.SchemeId,
            stateBefore,
            requestedSettings,
            stateAfter,
            PowerStateVerification.Compare(stateBefore.SchemeId, requestedSettings, stateAfter),
            Rollback: null,
            FailureMessage: null);
    }

    private static PowerOperationResult FailedPowerOperation(
        PowerStateSnapshot stateBefore,
        ProcessorPowerSettings requestedSettings,
        string failureMessage)
    {
        return SuccessfulPowerOperation(stateBefore, requestedSettings) with
        {
            IsSuccessful = false,
            FailureMessage = failureMessage
        };
    }

    private static PowerOperationResult SuccessfulRestoreOperation(
        PowerStateSnapshot stateBefore,
        ProcessorPowerSettings requestedSettings)
    {
        var stateAfter = stateBefore with
        {
            ProcessorMaximumAc = requestedSettings.ProcessorMaximumAc,
            ProcessorMaximumDc = requestedSettings.ProcessorMaximumDc,
            BoostModeAc = requestedSettings.BoostModeAc,
            BoostModeDc = requestedSettings.BoostModeDc
        };

        return new PowerOperationResult(
            PowerOperationKind.RestoreOriginalSnapshot,
            IsSuccessful: true,
            stateBefore.SchemeId,
            stateBefore,
            requestedSettings,
            stateAfter,
            PowerStateVerification.Compare(stateBefore.SchemeId, requestedSettings, stateAfter),
            Rollback: null,
            FailureMessage: null);
    }

    private static PowerOperationResult FailedRestoreOperation(string failureMessage)
    {
        return SuccessfulRestoreOperation(
            InitialPowerState(),
            ProcessorPowerSettings.FromSnapshot(InitialPowerState())) with
        {
            IsSuccessful = false,
            FailureMessage = failureMessage
        };
    }

    private sealed class FakeDiagnosticReportService : IDiagnosticReportService
    {
        private readonly Queue<AsyncGate> _generateGates = [];

        public DiagnosticReportResult Report { get; set; } = new(
            "Diagnostic report content.",
            "BootCampPerformanceControl-Diagnostics-Test.txt");

        public int GenerateCallCount { get; private set; }

        public void QueueGenerateGate(AsyncGate gate)
        {
            _generateGates.Enqueue(gate);
        }

        public async Task<DiagnosticReportResult> GenerateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenerateCallCount++;

            if (_generateGates.Count > 0)
            {
                await _generateGates.Dequeue().WaitAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Report;
        }
    }

    private sealed class FakeDiagnosticReportFileSaveService : IDiagnosticReportFileSaveService
    {
        public bool SaveResult { get; set; } = true;

        public Exception? SaveException { get; set; }

        public DiagnosticReportResult? LastReport { get; private set; }

        public int SaveCallCount { get; private set; }

        public Task<bool> SaveAsync(
            DiagnosticReportResult report,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCallCount++;
            LastReport = report;

            if (SaveException is not null)
            {
                throw SaveException;
            }

            return Task.FromResult(SaveResult);
        }
    }

    private sealed class FakeFanControlService : IFanControlService
    {
        public FanControlStatus GetStatus()
        {
            return new FanControlStatus(
                IsAvailable: false,
                "Unavailable in tests.");
        }
    }

    private sealed class FakeHardwareDetectionService : IHardwareDetectionService
    {
        private readonly Queue<AsyncGate> _detectGates = [];
        private readonly Queue<ModelVerificationResult> _verificationResults;
        private ModelVerificationResult _currentVerificationResult;

        public FakeHardwareDetectionService(params ModelVerificationResult[] verificationResults)
        {
            _verificationResults = new Queue<ModelVerificationResult>(verificationResults);
            _currentVerificationResult = verificationResults.Length == 0
                ? ModelVerificationResult.Unknown()
                : verificationResults[0];
        }

        public int DetectCallCount { get; private set; }

        public int VerifyModelCallCount { get; private set; }

        public void QueueDetectGate(AsyncGate gate)
        {
            _detectGates.Enqueue(gate);
        }

        public async Task<HardwareSnapshot> DetectAsync(CancellationToken cancellationToken)
        {
            DetectCallCount++;

            if (_detectGates.Count > 0)
            {
                await _detectGates.Dequeue().WaitAsync();
            }

            if (_verificationResults.Count > 0)
            {
                _currentVerificationResult = _verificationResults.Peek();
            }

            return new HardwareSnapshot(
                new ComputerSystemInfo(
                    _currentVerificationResult.Manufacturer,
                    _currentVerificationResult.Model,
                    "x64-based PC"),
                Processor: null,
                VideoControllers: [],
                OperatingSystem: null,
                DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
        }

        public ModelVerificationResult VerifyModel(HardwareSnapshot snapshot)
        {
            VerifyModelCallCount++;

            if (_verificationResults.Count > 0)
            {
                _currentVerificationResult = _verificationResults.Dequeue();
            }

            return _currentVerificationResult;
        }
    }

    private sealed class FakePowerManagementService : IPowerManagementService
    {
        private readonly Queue<AsyncGate> _readGates = [];
        private readonly Queue<Exception> _readExceptions = [];
        private readonly Queue<PowerStateSnapshot> _readStates;
        private readonly PowerOperationResult _applyResult;
        private PowerStateSnapshot _lastReadState;

        public FakePowerManagementService(params PowerStateSnapshot[] readStates)
            : this(
                FailedPowerOperation(
                    readStates.Length == 0 ? InitialPowerState() : readStates[0],
                    new ProcessorPowerSettings(95, 95, 0, 0),
                    "Apply should not have been called."),
                readStates)
        {
        }

        public FakePowerManagementService(
            PowerOperationResult applyResult,
            params PowerStateSnapshot[] readStates)
        {
            _readStates = new Queue<PowerStateSnapshot>(readStates);
            _lastReadState = readStates.Length == 0
                ? InitialPowerState()
                : readStates[^1];
            _applyResult = applyResult;
            RestoreResult = FailedRestoreOperation("Restore should not have been called.");
        }

        public IRestoreSnapshotStore? RestoreSnapshotStore { get; set; }

        public PowerOperationResult RestoreResult { get; set; }

        public int ReadCurrentStateCallCount { get; private set; }

        public int GuardedApplyCallCount { get; private set; }

        public int UnguardedApplyCallCount { get; private set; }

        public int RestoreOriginalSettingsCallCount { get; private set; }

        public ProcessorPowerSettings? LastGuardedSettings { get; private set; }

        public PowerStateSnapshot? LastExpectedStateBefore { get; private set; }

        public bool SaveRestoreSnapshotBeforeGuardedApplyResult { get; set; }

        public Exception? GuardedApplyException { get; set; }

        public Exception? RestoreException { get; set; }

        public void QueueReadGate(AsyncGate gate)
        {
            _readGates.Enqueue(gate);
        }

        public void QueueReadException(Exception exception)
        {
            _readExceptions.Enqueue(exception);
        }

        public async Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
        {
            ReadCurrentStateCallCount++;

            if (_readGates.Count > 0)
            {
                await _readGates.Dequeue().WaitAsync();
            }

            if (_readStates.Count == 0 && _readExceptions.Count > 0)
            {
                throw _readExceptions.Dequeue();
            }

            if (_readStates.Count > 0)
            {
                _lastReadState = _readStates.Dequeue();
            }

            return _lastReadState;
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            CancellationToken cancellationToken)
        {
            UnguardedApplyCallCount++;
            return Task.FromResult(_applyResult);
        }

        public async Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            PowerStateSnapshot expectedStateBefore,
            CancellationToken cancellationToken)
        {
            GuardedApplyCallCount++;
            LastGuardedSettings = requestedSettings;
            LastExpectedStateBefore = expectedStateBefore;

            if ((_applyResult.IsSuccessful || SaveRestoreSnapshotBeforeGuardedApplyResult)
                && RestoreSnapshotStore is not null)
            {
                await RestoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
                    expectedStateBefore,
                    cancellationToken);
            }

            if (GuardedApplyException is not null)
            {
                throw GuardedApplyException;
            }

            return _applyResult;
        }

        public async Task<PowerOperationResult> RestoreOriginalSettingsAsync(CancellationToken cancellationToken)
        {
            RestoreOriginalSettingsCallCount++;

            if (RestoreException is not null)
            {
                throw RestoreException;
            }

            if (RestoreResult.IsSuccessful && RestoreSnapshotStore is not null)
            {
                await RestoreSnapshotStore.ClearOriginalRestoreSnapshotAsync(cancellationToken);
            }

            return RestoreResult;
        }
    }

    private sealed class AsyncGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitAsync()
        {
            _entered.TrySetResult();
            await _release.Task;
        }

        public async Task WaitUntilEnteredAsync()
        {
            await _entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void Release()
        {
            _release.TrySetResult();
        }
    }

    private sealed class FakeUserConfirmationService : IUserConfirmationService
    {
        public bool Result { get; set; } = true;
        public int CallCount { get; private set; }
        public string? LastModelName { get; private set; }

        public bool ConfirmUntestedModelApply(string modelName)
        {
            CallCount++;
            LastModelName = modelName;
            return Result;
        }
    }
}
