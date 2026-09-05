using System.Buffers.Binary;
using System.Reflection;
using BootCampPerformanceControl.ApplicationSettings;
using BootCampPerformanceControl.Diagnostics;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.BackendActivation;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;
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
    public void ApplicationOptions_AreLoadedWithoutWritingSettings()
    {
        var optionsService = new FakeApplicationOptionsService
        {
            Options = new ApplicationOptionsSnapshot(
                ApplicationCloseBehavior.ExitApplication,
                StartWithWindows: true,
                StartMinimizedToTray: true)
        };
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            applicationOptionsService: optionsService);

        Assert.False(viewModel.MinimizeToTrayOnClose);
        Assert.True(viewModel.ExitApplicationOnClose);
        Assert.True(viewModel.StartWithWindows);
        Assert.True(viewModel.StartMinimizedToTray);
        Assert.Equal(1, optionsService.LoadCallCount);
        Assert.Equal(0, optionsService.SetCloseBehaviorCallCount);
        Assert.Equal(0, optionsService.SetStartWithWindowsCallCount);
        Assert.Equal(0, optionsService.SetStartMinimizedToTrayCallCount);
    }

    [Fact]
    public void ApplicationOptions_ChangesPersistAndUpdateTheViewModel()
    {
        var optionsService = new FakeApplicationOptionsService();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            applicationOptionsService: optionsService);

        Assert.True(viewModel.MinimizeToTrayOnClose);
        Assert.False(viewModel.ExitApplicationOnClose);
        Assert.False(viewModel.StartWithWindows);
        Assert.False(viewModel.StartMinimizedToTray);

        viewModel.ExitApplicationOnClose = true;
        viewModel.StartWithWindows = true;
        viewModel.StartMinimizedToTray = true;

        Assert.False(viewModel.MinimizeToTrayOnClose);
        Assert.True(viewModel.ExitApplicationOnClose);
        Assert.True(viewModel.StartWithWindows);
        Assert.True(viewModel.StartMinimizedToTray);
        Assert.Equal(ApplicationCloseBehavior.ExitApplication, optionsService.LastCloseBehavior);
        Assert.True(optionsService.LastStartWithWindows);
        Assert.True(optionsService.LastStartMinimizedToTray);
        Assert.Equal(1, optionsService.SetCloseBehaviorCallCount);
        Assert.Equal(1, optionsService.SetStartWithWindowsCallCount);
        Assert.Equal(1, optionsService.SetStartMinimizedToTrayCallCount);
    }

    [Fact]
    public void ApplicationOptions_WriteFailureRevertsDisplayedValuesAndLogsError()
    {
        var expectedException = new InvalidOperationException("Registry unavailable.");
        var optionsService = new FakeApplicationOptionsService
        {
            SetException = expectedException
        };
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            logger: logger,
            applicationOptionsService: optionsService);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        viewModel.ExitApplicationOnClose = true;
        viewModel.StartWithWindows = true;
        viewModel.StartMinimizedToTray = true;

        Assert.True(viewModel.MinimizeToTrayOnClose);
        Assert.False(viewModel.ExitApplicationOnClose);
        Assert.False(viewModel.StartWithWindows);
        Assert.False(viewModel.StartMinimizedToTray);
        Assert.Contains(nameof(MainViewModel.MinimizeToTrayOnClose), changedProperties);
        Assert.Contains(nameof(MainViewModel.ExitApplicationOnClose), changedProperties);
        Assert.Contains(nameof(MainViewModel.StartWithWindows), changedProperties);
        Assert.Contains(nameof(MainViewModel.StartMinimizedToTray), changedProperties);
        Assert.Equal(3, logger.Errors.Count);
        Assert.All(logger.Errors, error => Assert.Same(expectedException, error.Exception));
    }

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
    public void Constructor_DoesNotPerformFanHardwareIo()
    {
        var hardwareDetectionService = new FakeHardwareDetectionService(
            VerifiedMacBookPro16_1());
        var fanControlService = new FakeFanControlService();
        var elevationLauncher = new FakeAppleSmcBackendElevationLauncher();

        var viewModel = CreateViewModel(
            hardwareDetectionService,
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: fanControlService,
            elevationLauncher: elevationLauncher);

        Assert.Equal(FanBackendState.NotChecked, viewModel.FanStatus.BackendState);
        Assert.Equal(FanSafetyState.NotChecked, viewModel.FanStatus.SafetyState);
        Assert.Equal(0, hardwareDetectionService.DetectCallCount);
        Assert.Equal(0, fanControlService.ReadStatusCallCount);
        Assert.Equal(0, elevationLauncher.LaunchCallCount);
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
    public async Task Refresh_WithSuccessfulFanRead_UpdatesFanStatusAfterHardwareDetection()
    {
        var verifiedFanStatus = VerifiedFanStatus();
        var hardwareDetectionService = new FakeHardwareDetectionService(
            VerifiedMacBookPro16_1());
        var fanControlService = new FakeFanControlService(
            verifiedFanStatus);
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            powerManagementService,
            logger: logger,
            fanControlService: fanControlService);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(verifiedFanStatus, viewModel.FanStatus);
        Assert.Equal(1840f, viewModel.FanStatus.Fans[0].Reading.ActualRpm);
        Assert.Equal(1691f, viewModel.FanStatus.Fans[1].Reading.ActualRpm);
        Assert.Equal([VerifiedHardwareModels.MacBookPro16_1], fanControlService.ReadModels);
        Assert.Equal(1, hardwareDetectionService.DetectCallCount);
        Assert.Equal(1, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal("Refresh completed.", viewModel.StatusMessage);
        Assert.Contains(
            logger.InformationMessages,
            message => message.Contains("Fan read started", StringComparison.Ordinal));
        Assert.Contains(
            logger.InformationMessages,
            message => message.Contains("Fan read completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refresh_FanReadFailure_ReportsErrorAndStillReadsPowerState()
    {
        var fanControlService = new FakeFanControlService();
        fanControlService.QueueReadException(
            new InvalidOperationException("AppleSMC device is busy."));
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            logger: logger,
            fanControlService: fanControlService);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanBackendState.Error, viewModel.FanStatus.BackendState);
        Assert.Equal(FanSafetyState.Error, viewModel.FanStatus.SafetyState);
        Assert.Equal(1, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal(
            "Refresh completed with errors. Check the log for details.",
            viewModel.StatusMessage);
        var error = Assert.Single(logger.Errors);
        Assert.Contains("Fan read failed", error.Message, StringComparison.Ordinal);
        Assert.Equal("AppleSMC device is busy.", error.Exception.Message);
    }

    [Fact]
    public async Task Refresh_FanCancellation_ReportsCanceledWithoutNormalFanFailure()
    {
        var fanControlService = new FakeFanControlService();
        fanControlService.QueueReadException(
            new OperationCanceledException("Fan read canceled."));
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            logger: logger,
            fanControlService: fanControlService);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal("Refresh canceled.", viewModel.StatusMessage);
        Assert.Equal(FanBackendState.NotChecked, viewModel.FanStatus.BackendState);
        Assert.Equal(0, powerManagementService.ReadCurrentStateCallCount);
        Assert.Empty(logger.Errors);
        Assert.DoesNotContain(
            logger.InformationMessages,
            message => message.Contains("Fan read failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refresh_HardwareDetectionFailure_SkipsFanReadAndStillReadsPowerState()
    {
        var hardwareDetectionService = new FakeHardwareDetectionService(
            VerifiedMacBookPro16_1())
        {
            DetectException = new InvalidOperationException("Detection failed.")
        };
        var fanControlService = new FakeFanControlService();
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            powerManagementService,
            fanControlService: fanControlService);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(0, fanControlService.ReadStatusCallCount);
        Assert.Equal(1, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal(FanBackendState.Unavailable, viewModel.FanStatus.BackendState);
        Assert.Equal(FanSafetyState.MonitoringUnavailable, viewModel.FanStatus.SafetyState);
        Assert.Equal(
            "Refresh completed with errors. Check the log for details.",
            viewModel.StatusMessage);
    }

    [Theory]
    [InlineData(FanBackendState.NotInstalled)]
    [InlineData(FanBackendState.InstalledStopped)]
    public async Task Refresh_ExpectedBackendUnavailableState_DoesNotMarkRefreshAsError(
        FanBackendState backendState)
    {
        var fanControlService = new FakeFanControlService(
            FanControlStatus.CreateUnavailable(
                backendState,
                FanSafetyState.MonitoringUnavailable,
                "Expected backend state in test."));
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            logger: logger,
            fanControlService: fanControlService);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(backendState, viewModel.FanStatus.BackendState);
        Assert.Equal("Refresh completed.", viewModel.StatusMessage);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task EnableFanMonitoringCommand_IsAvailableOnlyForInstalledStoppedAndVerifiedFanIdentity()
    {
        var elevationLauncher = new FakeAppleSmcBackendElevationLauncher();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: new FakeFanControlService(InstalledStoppedFanStatus()),
            elevationLauncher: elevationLauncher);

        Assert.False(viewModel.IsFanMonitoringActivationAvailable);
        Assert.False(viewModel.EnableFanMonitoringCommand.CanExecute(null));

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.True(viewModel.IsFanMonitoringActivationAvailable);
        Assert.True(viewModel.EnableFanMonitoringCommand.CanExecute(null));
        Assert.Equal(0, elevationLauncher.LaunchCallCount);
    }

    [Theory]
    [InlineData(FanBackendState.NotChecked)]
    [InlineData(FanBackendState.NotApplicable)]
    [InlineData(FanBackendState.NotInstalled)]
    [InlineData(FanBackendState.Running)]
    [InlineData(FanBackendState.Busy)]
    [InlineData(FanBackendState.AccessDenied)]
    [InlineData(FanBackendState.Transitional)]
    [InlineData(FanBackendState.Unavailable)]
    [InlineData(FanBackendState.Error)]
    public async Task EnableFanMonitoringCommand_IsUnavailableForOtherBackendStates(
        FanBackendState backendState)
    {
        var status = backendState == FanBackendState.Running
            ? VerifiedFanStatus()
            : FanControlStatus.CreateUnavailable(
                backendState,
                FanSafetyState.MonitoringUnavailable,
                "Backend is unavailable in the test.");
        var elevationLauncher = new FakeAppleSmcBackendElevationLauncher();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: new FakeFanControlService(status),
            elevationLauncher: elevationLauncher);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.False(viewModel.IsFanMonitoringActivationAvailable);
        Assert.False(viewModel.EnableFanMonitoringCommand.CanExecute(null));
        viewModel.EnableFanMonitoringCommand.Execute(null);
        Assert.Equal(0, elevationLauncher.LaunchCallCount);
    }

    [Fact]
    public async Task EnableFanMonitoringCommand_IsAvailableForSupportedIntelMacReadOnlyDiscovery()
    {
        var unsupportedIdentity = new ModelVerificationResult(
            "Apple Inc.",
            "MacBookPro15,1",
            PlatformSupportStatus.SupportedIntelMac,
            ModelValidationLevel.NotIndividuallyTested,
            "Supported Intel Mac without verified fan activation identity.");
        var elevationLauncher = new FakeAppleSmcBackendElevationLauncher();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(unsupportedIdentity),
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: new FakeFanControlService(InstalledStoppedFanStatus()),
            elevationLauncher: elevationLauncher);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanBackendState.InstalledStopped, viewModel.FanStatus.BackendState);
        Assert.True(viewModel.IsFanMonitoringActivationAvailable);
        Assert.True(viewModel.EnableFanMonitoringCommand.CanExecute(null));
        viewModel.EnableFanMonitoringCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        Assert.Equal(1, elevationLauncher.LaunchCallCount);
    }

    [Fact]
    public async Task EnableFanMonitoringCommand_SuccessUsesBusyInterlockAndFreshParentRead()
    {
        var launchGate = new AsyncGate();
        var elevationLauncher = new FakeAppleSmcBackendElevationLauncher();
        elevationLauncher.QueueLaunchGate(launchGate);
        elevationLauncher.QueueResult(CompletedElevationResult());
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var fanControlService = new FakeFanControlService(
            InstalledStoppedFanStatus(),
            VerifiedFanStatus());
        var hardwareDetectionService = new FakeHardwareDetectionService(
            VerifiedMacBookPro16_1());
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            powerManagementService,
            fanControlService: fanControlService,
            elevationLauncher: elevationLauncher);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        var powerReadsBeforeActivation = powerManagementService.ReadCurrentStateCallCount;
        var detectedProfileBeforeActivation = viewModel.DetectedProfileState;

        viewModel.EnableFanMonitoringCommand.Execute(null);
        await launchGate.WaitUntilEnteredAsync();

        Assert.True(viewModel.IsBusy);
        Assert.True(viewModel.IsFanMonitoringActivationAvailable);
        Assert.False(viewModel.EnableFanMonitoringCommand.CanExecute(null));
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.Equal(
            "Requesting administrator permission to enable fan monitoring...",
            viewModel.StatusMessage);

        launchGate.Release();
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, elevationLauncher.LaunchCallCount);
        Assert.Equal(2, fanControlService.ReadStatusCallCount);
        Assert.Equal(FanBackendState.Running, viewModel.FanStatus.BackendState);
        Assert.True(viewModel.FanStatus.IsAvailable);
        Assert.Equal("Fan monitoring enabled.", viewModel.StatusMessage);
        Assert.False(viewModel.IsFanMonitoringActivationAvailable);
        Assert.Equal(powerReadsBeforeActivation, powerManagementService.ReadCurrentStateCallCount);
        Assert.Equal(detectedProfileBeforeActivation, viewModel.DetectedProfileState);
        Assert.Equal(1, hardwareDetectionService.DetectCallCount);
    }

    [Fact]
    public async Task EnableFanMonitoringCommand_CompletedHelperUsesObservedParentStateAsSourceOfTruth()
    {
        var elevationLauncher = new FakeAppleSmcBackendElevationLauncher();
        elevationLauncher.QueueResult(CompletedElevationResult());
        var fanControlService = new FakeFanControlService(
            InstalledStoppedFanStatus(),
            InstalledStoppedFanStatus("Still stopped after helper completion."));
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: fanControlService,
            elevationLauncher: elevationLauncher);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        viewModel.EnableFanMonitoringCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(2, fanControlService.ReadStatusCallCount);
        Assert.Equal(FanBackendState.InstalledStopped, viewModel.FanStatus.BackendState);
        Assert.Contains("Installed, stopped", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.NotEqual("Fan monitoring enabled.", viewModel.StatusMessage);
        Assert.True(viewModel.IsFanMonitoringActivationAvailable);
    }

    [Fact]
    public async Task EnableFanMonitoringCommand_CompletedFailureOutcomeStillPerformsParentRead()
    {
        var elevationLauncher = new FakeAppleSmcBackendElevationLauncher();
        elevationLauncher.QueueResult(CompletedElevationResult(
            AppleSmcBackendActivationOutcome.AccessDenied,
            exitCode: 13));
        var fanControlService = new FakeFanControlService(
            InstalledStoppedFanStatus(),
            VerifiedFanStatus());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: fanControlService,
            elevationLauncher: elevationLauncher);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        viewModel.EnableFanMonitoringCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(2, fanControlService.ReadStatusCallCount);
        Assert.True(viewModel.FanStatus.IsAvailable);
        Assert.Equal("Fan monitoring enabled.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task EnableFanMonitoringCommand_UnknownHelperExitStillPerformsParentRead()
    {
        var helperException = new InvalidOperationException("Unknown helper exit code 99.");
        var logger = new TestApplicationLogger();
        var elevationLauncher = new FakeAppleSmcBackendElevationLauncher();
        elevationLauncher.QueueResult(new AppleSmcBackendElevationResult(
            AppleSmcBackendElevationOutcome.Failed,
            HelperOutcome: null,
            ExitCode: 99,
            Exception: helperException));
        var fanControlService = new FakeFanControlService(
            InstalledStoppedFanStatus(),
            InstalledStoppedFanStatus("Still stopped after the unknown helper exit."));
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            logger: logger,
            fanControlService: fanControlService,
            elevationLauncher: elevationLauncher);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        viewModel.EnableFanMonitoringCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(2, fanControlService.ReadStatusCallCount);
        Assert.Equal(FanBackendState.InstalledStopped, viewModel.FanStatus.BackendState);
        Assert.Contains("Installed, stopped", viewModel.StatusMessage, StringComparison.Ordinal);
        var error = Assert.Single(logger.Errors);
        Assert.Same(helperException, error.Exception);
    }

    [Fact]
    public async Task EnableFanMonitoringCommand_UserCanceledIsExpectedAndDoesNotReadOrLogError()
    {
        var logger = new TestApplicationLogger();
        var elevationLauncher = new FakeAppleSmcBackendElevationLauncher();
        elevationLauncher.QueueResult(UserCanceledElevationResult());
        var fanControlService = new FakeFanControlService(InstalledStoppedFanStatus());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            logger: logger,
            fanControlService: fanControlService,
            elevationLauncher: elevationLauncher);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        viewModel.EnableFanMonitoringCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, elevationLauncher.LaunchCallCount);
        Assert.Equal(1, fanControlService.ReadStatusCallCount);
        Assert.Equal(FanBackendState.InstalledStopped, viewModel.FanStatus.BackendState);
        Assert.Equal("Fan monitoring was not enabled.", viewModel.StatusMessage);
        Assert.Empty(logger.Errors);
        Assert.Contains(
            logger.InformationMessages,
            message => message.Contains("canceled by the user", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnableFanMonitoringCommand_LaunchFailureIsReportedWithoutParentRead()
    {
        var launchException = new InvalidOperationException("Elevation launch failed in test.");
        var logger = new TestApplicationLogger();
        var elevationLauncher = new FakeAppleSmcBackendElevationLauncher();
        elevationLauncher.QueueResult(FailedElevationResult(
            launchException.Message,
            launchException));
        var fanControlService = new FakeFanControlService(InstalledStoppedFanStatus());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            logger: logger,
            fanControlService: fanControlService,
            elevationLauncher: elevationLauncher);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        viewModel.EnableFanMonitoringCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, fanControlService.ReadStatusCallCount);
        Assert.Equal(
            "Fan monitoring could not be enabled. Check the log for details.",
            viewModel.StatusMessage);
        var error = Assert.Single(logger.Errors);
        Assert.Same(launchException, error.Exception);
    }

    [Fact]
    public async Task EnableFanMonitoringCommand_IsInterlockedWithInFlightLivePoll()
    {
        var pollingDelay = new ManualFanPollingDelay();
        var elevationLauncher = new FakeAppleSmcBackendElevationLauncher();
        elevationLauncher.QueueResult(CompletedElevationResult());
        var fanControlService = new FakeFanControlService(
            InstalledStoppedFanStatus(),
            InstalledStoppedFanStatus("Live poll remains stopped."),
            VerifiedFanStatus());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: fanControlService,
            elevationLauncher: elevationLauncher,
            fanPollingDelayAsync: pollingDelay.DelayAsync);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var liveReadGate = new AsyncGate();
        fanControlService.QueueReadGate(liveReadGate);
        viewModel.StartFanMonitoring();
        pollingDelay.Advance();
        await liveReadGate.WaitUntilEnteredAsync();

        viewModel.EnableFanMonitoringCommand.Execute(null);
        Assert.True(viewModel.IsBusy);
        Assert.Equal(0, elevationLauncher.LaunchCallCount);
        Assert.Equal(1, fanControlService.MaximumConcurrentReadCount);

        liveReadGate.Release();
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, elevationLauncher.LaunchCallCount);
        Assert.Equal(3, fanControlService.ReadStatusCallCount);
        Assert.Equal(1, fanControlService.MaximumConcurrentReadCount);
        Assert.True(viewModel.FanStatus.IsAvailable);

        await WaitUntilAsync(() => pollingDelay.RequestCount == 2);
        pollingDelay.Advance();
        await WaitUntilAsync(
            () => fanControlService.ReadStatusCallCount == 4
                && pollingDelay.RequestCount == 3);
        Assert.Equal(1, fanControlService.MaximumConcurrentReadCount);

        await viewModel.StopFanMonitoringAsync();
    }

    [Fact]
    public async Task StopFanMonitoringAsync_CancelsPendingActivationWithoutKillingHelper()
    {
        var launchGate = new AsyncGate();
        var elevationLauncher = new FakeAppleSmcBackendElevationLauncher();
        elevationLauncher.QueueLaunchGate(launchGate);
        elevationLauncher.QueueResult(CompletedElevationResult());
        var fanControlService = new FakeFanControlService(InstalledStoppedFanStatus());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: fanControlService,
            elevationLauncher: elevationLauncher);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        viewModel.EnableFanMonitoringCommand.Execute(null);
        await launchGate.WaitUntilEnteredAsync();

        await viewModel.StopFanMonitoringAsync();
        await WaitForIdleAsync(viewModel);

        Assert.True(elevationLauncher.CancellationObserved);
        Assert.Equal(1, fanControlService.ReadStatusCallCount);
        Assert.Equal("Fan monitoring activation canceled.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task FanMonitoring_DoesNotPollBeforeStartOrWithoutVerifiedIdentity()
    {
        var pollingDelay = new ManualFanPollingDelay();
        var fanControlService = new FakeFanControlService(VerifiedFanStatus());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: fanControlService,
            fanPollingDelayAsync: pollingDelay.DelayAsync);

        Assert.Equal(0, pollingDelay.RequestCount);
        Assert.Equal(0, fanControlService.ReadStatusCallCount);

        viewModel.StartFanMonitoring();
        Assert.Equal(1, pollingDelay.RequestCount);
        pollingDelay.Advance();
        await WaitUntilAsync(() => pollingDelay.RequestCount == 2);

        Assert.Equal(0, fanControlService.ReadStatusCallCount);

        await viewModel.StopFanMonitoringAsync();
    }

    [Fact]
    public async Task FanMonitoring_SuccessivePollsUpdateStructuredRpmWithoutOverlappingOrLogSpam()
    {
        var pollingDelay = new ManualFanPollingDelay();
        var fanControlService = new FakeFanControlService(
            VerifiedFanStatus(1800f, 1650f),
            VerifiedFanStatus(1900f, 1750f),
            VerifiedFanStatus(2000f, 1850f));
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            logger: logger,
            fanControlService: fanControlService,
            fanPollingDelayAsync: pollingDelay.DelayAsync);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        Assert.Equal(1800f, viewModel.FanStatus.Fans[0].Reading.ActualRpm);

        viewModel.StartFanMonitoring();
        pollingDelay.Advance();
        await WaitUntilAsync(
            () => fanControlService.ReadStatusCallCount == 2
                && pollingDelay.RequestCount == 2);
        Assert.Equal(1900f, viewModel.FanStatus.Fans[0].Reading.ActualRpm);
        Assert.Equal(1750f, viewModel.FanStatus.Fans[1].Reading.ActualRpm);

        pollingDelay.Advance();
        await WaitUntilAsync(
            () => fanControlService.ReadStatusCallCount == 3
                && pollingDelay.RequestCount == 3);
        Assert.Equal(2000f, viewModel.FanStatus.Fans[0].Reading.ActualRpm);
        Assert.Equal(1850f, viewModel.FanStatus.Fans[1].Reading.ActualRpm);
        Assert.Equal(1, fanControlService.MaximumConcurrentReadCount);
        Assert.Single(
            logger.InformationMessages,
            message => message.Contains(
                "Fan monitoring state changed",
                StringComparison.Ordinal));

        await viewModel.StopFanMonitoringAsync();
        var callsAfterStop = fanControlService.ReadStatusCallCount;
        pollingDelay.Advance();
        await Task.Yield();
        Assert.Equal(callsAfterStop, fanControlService.ReadStatusCallCount);
        Assert.Equal(3, pollingDelay.RequestCount);
    }

    [Fact]
    public async Task FanMonitoring_InFlightPollAndFullRefreshAreInterlocked()
    {
        var pollingDelay = new ManualFanPollingDelay();
        var fanControlService = new FakeFanControlService(VerifiedFanStatus());
        var hardwareDetectionService = new FakeHardwareDetectionService(
            VerifiedMacBookPro16_1(),
            VerifiedMacBookPro16_1());
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: fanControlService,
            fanPollingDelayAsync: pollingDelay.DelayAsync);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var liveReadGate = new AsyncGate();
        fanControlService.QueueReadGate(liveReadGate);
        viewModel.StartFanMonitoring();
        pollingDelay.Advance();
        await liveReadGate.WaitUntilEnteredAsync();

        viewModel.RefreshCommand.Execute(null);
        Assert.True(viewModel.IsBusy);
        Assert.Equal(1, hardwareDetectionService.DetectCallCount);
        Assert.Equal(1, fanControlService.MaximumConcurrentReadCount);

        liveReadGate.Release();
        await WaitForIdleAsync(viewModel);

        Assert.Equal(2, hardwareDetectionService.DetectCallCount);
        Assert.Equal(3, fanControlService.ReadStatusCallCount);
        Assert.Equal(1, fanControlService.MaximumConcurrentReadCount);

        await viewModel.StopFanMonitoringAsync();
    }

    [Fact]
    public async Task FanMonitoring_SkipsTickWhileFullRefreshIsBusy()
    {
        var pollingDelay = new ManualFanPollingDelay();
        var fanControlService = new FakeFanControlService(VerifiedFanStatus());
        var hardwareDetectionService = new FakeHardwareDetectionService(
            VerifiedMacBookPro16_1(),
            VerifiedMacBookPro16_1());
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: fanControlService,
            fanPollingDelayAsync: pollingDelay.DelayAsync);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var refreshDetectionGate = new AsyncGate();
        hardwareDetectionService.QueueDetectGate(refreshDetectionGate);
        viewModel.RefreshCommand.Execute(null);
        await refreshDetectionGate.WaitUntilEnteredAsync();

        viewModel.StartFanMonitoring();
        pollingDelay.Advance();
        await WaitUntilAsync(() => pollingDelay.RequestCount == 2);

        Assert.True(viewModel.IsBusy);
        Assert.Equal(1, fanControlService.ReadStatusCallCount);

        refreshDetectionGate.Release();
        await WaitForIdleAsync(viewModel);
        Assert.Equal(2, fanControlService.ReadStatusCallCount);

        await viewModel.StopFanMonitoringAsync();
    }

    [Fact]
    public async Task FanMonitoring_SkipsTickWhileFanGateIsHeldDuringRecovery()
    {
        var pollingDelay = new ManualFanPollingDelay();
        var fanControlService = new FakeFanControlService(VerifiedFanStatus(), VerifiedFanStatus());
        var hardwareDetectionService = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var recoveryGate = new AsyncGate();
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = async () =>
            {
                await recoveryGate.WaitAsync();
                ownershipStore.Marker = null;
                return new TestFanExecutionSession(
                    overrideCoordinator: new TestFanOverrideCoordinator
                    {
                        RecoverHandler = (m, cap, ct) => Task.FromResult(new FanOverrideRecoveryDecision(
                            FanOverrideRecoveryAction.RestoreAppleAuto,
                            "Restored."))
                    });
            }
        };
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: fanControlService,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore,
            fanPollingDelayAsync: pollingDelay.DelayAsync);

        viewModel.RefreshCommand.Execute(null);
        await recoveryGate.WaitUntilEnteredAsync();

        Assert.True(viewModel.IsBusy);

        viewModel.StartFanMonitoring();
        pollingDelay.Advance();
        await WaitUntilAsync(() => pollingDelay.RequestCount == 2);

        Assert.Equal(0, fanControlService.ReadStatusCallCount);

        recoveryGate.Release();
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanRecoveryState.None, viewModel.RecoveryState);

        await viewModel.StopFanMonitoringAsync();
    }

    [Fact]
    public async Task FanMonitoring_HardwareDetectionFailureClearsStaleModel()
    {
        var pollingDelay = new ManualFanPollingDelay();
        var fanControlService = new FakeFanControlService(VerifiedFanStatus());
        var hardwareDetectionService = new FakeHardwareDetectionService(
            VerifiedMacBookPro16_1());
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: fanControlService,
            fanPollingDelayAsync: pollingDelay.DelayAsync);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        viewModel.StartFanMonitoring();

        hardwareDetectionService.DetectException = new InvalidOperationException(
            "Detection failed after identity was established.");
        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanBackendState.Unavailable, viewModel.FanStatus.BackendState);
        Assert.Equal(1, fanControlService.ReadStatusCallCount);

        pollingDelay.Advance();
        await WaitUntilAsync(() => pollingDelay.RequestCount == 2);
        Assert.Equal(1, fanControlService.ReadStatusCallCount);

        await viewModel.StopFanMonitoringAsync();
    }

    [Fact]
    public async Task FanMonitoring_StopCancelsInFlightReadAndPreventsFurtherPolls()
    {
        var pollingDelay = new ManualFanPollingDelay();
        var fanControlService = new FakeFanControlService(VerifiedFanStatus());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: fanControlService,
            fanPollingDelayAsync: pollingDelay.DelayAsync);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var liveReadGate = new AsyncGate();
        fanControlService.QueueReadGate(liveReadGate);
        viewModel.StartFanMonitoring();
        pollingDelay.Advance();
        await liveReadGate.WaitUntilEnteredAsync();

        await viewModel.StopFanMonitoringAsync();

        Assert.Equal(2, fanControlService.ReadStatusCallCount);
        Assert.Equal(1, pollingDelay.RequestCount);
    }

    [Fact]
    public async Task ApplyProfileAsync_WaitsForInFlightLivePoll()
    {
        var pollingDelay = new ManualFanPollingDelay();
        var fanControlService = new FakeFanControlService(
            VerifiedFanStatus(),
            VerifiedFanStatus());
        var sessionFactory = new TestFanExecutionSessionFactory();
        var powerManagementService = new FakePowerManagementService(
            SuccessfulPowerOperation(InitialPowerState(), new ProcessorPowerSettings(95, 95, 0, 0)),
            InitialPowerState());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            fanControlService: fanControlService,
            fanPollingDelayAsync: pollingDelay.DelayAsync,
            fanExecutionSessionFactory: sessionFactory);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var liveReadGate = new AsyncGate();
        fanControlService.QueueReadGate(liveReadGate);
        viewModel.StartFanMonitoring();
        pollingDelay.Advance();
        await liveReadGate.WaitUntilEnteredAsync();

        // Trigger Apply while live poll is in-flight holding the fan gate
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);
        Assert.True(viewModel.IsBusy);
        Assert.Equal(0, sessionFactory.OpenCallCount);

        // Release polling read; Apply now acquires gate and opens write session
        liveReadGate.Release();
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.False(viewModel.IsBusy);
        Assert.Contains("applied successfully", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        await viewModel.StopFanMonitoringAsync();
    }

    [Fact]
    public async Task RestoreAsync_WaitsForInFlightLivePoll()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var originalSnapshot = InitialPowerState();
        var powerManagementService = new FakePowerManagementService(
            GamingOptimisedPowerState(),
            originalSnapshot);
        powerManagementService.RestoreResult = SuccessfulRestoreOperation(
            GamingOptimisedPowerState(),
            ProcessorPowerSettings.FromSnapshot(originalSnapshot));
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);

        var pollingDelay = new ManualFanPollingDelay();
        var fanControlService = new FakeFanControlService(
            VerifiedFanStatus(),
            VerifiedFanStatus());
        var sessionFactory = new TestFanExecutionSessionFactory();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore,
            fanControlService: fanControlService,
            fanPollingDelayAsync: pollingDelay.DelayAsync,
            fanExecutionSessionFactory: sessionFactory);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var liveReadGate = new AsyncGate();
        fanControlService.QueueReadGate(liveReadGate);
        viewModel.StartFanMonitoring();
        pollingDelay.Advance();
        await liveReadGate.WaitUntilEnteredAsync();

        // Trigger Restore while live poll is in-flight holding the fan gate
        GetProfile(viewModel, "restore").Command!.Execute(null);
        Assert.True(viewModel.IsBusy);
        Assert.Equal(0, sessionFactory.OpenCallCount);

        // Release polling read; Restore now acquires gate and opens write session
        liveReadGate.Release();
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.False(viewModel.IsBusy);
        Assert.Contains("restored successfully", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        await viewModel.StopFanMonitoringAsync();
    }

    [Fact]
    public async Task FanPolling_SkipsCycleWhenApplyOwnsFanOperationGate()
    {
        var pollingDelay = new ManualFanPollingDelay();
        var fanControlService = new FakeFanControlService(
            VerifiedFanStatus(),
            VerifiedFanStatus());
        var applyGate = new AsyncGate();
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = async () =>
            {
                await applyGate.WaitAsync();
                return new TestFanExecutionSession();
            }
        };

        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()),
            fanControlService: fanControlService,
            fanPollingDelayAsync: pollingDelay.DelayAsync,
            fanExecutionSessionFactory: sessionFactory);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var readCountBefore = fanControlService.ReadStatusCallCount;

        viewModel.StartFanMonitoring();
        Assert.Equal(1, pollingDelay.RequestCount);

        // Start Apply which will enter OpenSessionHandler and wait on applyGate while owning the fan gate
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);
        await applyGate.WaitUntilEnteredAsync();
        Assert.True(viewModel.IsBusy);

        // Advance polling tick while Apply owns the gate
        pollingDelay.Advance();
        await WaitUntilAsync(() => pollingDelay.RequestCount == 2);

        // Polling skipped because gate was owned / IsBusy
        Assert.Equal(readCountBefore, fanControlService.ReadStatusCallCount);

        // Release Apply gate so it completes
        applyGate.Release();
        await WaitForIdleAsync(viewModel);

        Assert.False(viewModel.IsBusy);
        await viewModel.StopFanMonitoringAsync();
    }

    [Fact]
    public async Task FanPolling_SkipsCycleWhenRestoreOwnsFanOperationGate()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var originalSnapshot = InitialPowerState();
        var powerManagementService = new FakePowerManagementService(
            GamingOptimisedPowerState(),
            originalSnapshot);
        powerManagementService.RestoreResult = SuccessfulRestoreOperation(
            GamingOptimisedPowerState(),
            ProcessorPowerSettings.FromSnapshot(originalSnapshot));
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);

        var pollingDelay = new ManualFanPollingDelay();
        var fanControlService = new FakeFanControlService(
            VerifiedFanStatus(),
            VerifiedFanStatus());
        var restoreGate = new AsyncGate();
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = async () =>
            {
                await restoreGate.WaitAsync();
                return new TestFanExecutionSession();
            }
        };

        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore,
            fanControlService: fanControlService,
            fanPollingDelayAsync: pollingDelay.DelayAsync,
            fanExecutionSessionFactory: sessionFactory);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var readCountBefore = fanControlService.ReadStatusCallCount;

        viewModel.StartFanMonitoring();
        Assert.Equal(1, pollingDelay.RequestCount);

        // Start Restore which will wait on restoreGate while owning the fan gate
        GetProfile(viewModel, "restore").Command!.Execute(null);
        await restoreGate.WaitUntilEnteredAsync();
        Assert.True(viewModel.IsBusy);

        // Advance polling tick while Restore owns the gate
        pollingDelay.Advance();
        await WaitUntilAsync(() => pollingDelay.RequestCount == 2);

        // Polling skipped because gate was owned / IsBusy
        Assert.Equal(readCountBefore, fanControlService.ReadStatusCallCount);

        // Release Restore gate
        restoreGate.Release();
        await WaitForIdleAsync(viewModel);

        Assert.False(viewModel.IsBusy);
        await viewModel.StopFanMonitoringAsync();
    }

    [Fact]
    public async Task ApplyProfileAsync_Success_RefreshesFanStatusUnderGate()
    {
        var fanControlService = new FakeFanControlService(
            VerifiedFanStatus(1840f, 1691f),
            VerifiedFanStatus(5616f, 5200f));
        var powerManagementService = new FakePowerManagementService(
            SuccessfulPowerOperation(InitialPowerState(), new ProcessorPowerSettings(95, 95, 0, 0)),
            InitialPowerState());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            fanControlService: fanControlService);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var readCountBefore = fanControlService.ReadStatusCallCount;

        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(readCountBefore + 1, fanControlService.ReadStatusCallCount);
        Assert.Equal(5616f, viewModel.FanStatus.Fans[0].Reading.ActualRpm);
    }

    [Fact]
    public async Task RestoreAsync_Success_RefreshesFanStatusUnderGate()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var originalSnapshot = InitialPowerState();
        var powerManagementService = new FakePowerManagementService(
            GamingOptimisedPowerState(),
            originalSnapshot);
        powerManagementService.RestoreResult = SuccessfulRestoreOperation(
            GamingOptimisedPowerState(),
            ProcessorPowerSettings.FromSnapshot(originalSnapshot));
        await restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);

        var fanControlService = new FakeFanControlService(
            VerifiedFanStatus(5616f, 5200f),
            VerifiedFanStatus(1840f, 1691f));
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            restoreSnapshotStore,
            fanControlService: fanControlService);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var readCountBefore = fanControlService.ReadStatusCallCount;

        GetProfile(viewModel, "restore").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(readCountBefore + 1, fanControlService.ReadStatusCallCount);
        Assert.Equal(1840f, viewModel.FanStatus.Fans[0].Reading.ActualRpm);
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
    public void ReportCompatibilityIssueCommand_IsExposed()
    {
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()));

        Assert.NotNull(viewModel.ReportCompatibilityIssueCommand);
        Assert.True(viewModel.ReportCompatibilityIssueCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReportCompatibilityIssueCommand_GeneratesAndShowsReportWithKnownFanStatus()
    {
        var compatibilityReportService = new FakeCompatibilityReportService();
        var compatibilityReportDialogService = new FakeCompatibilityReportDialogService();
        var fanControlService = new FakeFanControlService(VerifiedFanStatus());
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService,
            fanControlService: fanControlService,
            compatibilityReportService: compatibilityReportService,
            compatibilityReportDialogService: compatibilityReportDialogService);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        var fanReadCountAfterRefresh = fanControlService.ReadStatusCallCount;

        viewModel.ReportCompatibilityIssueCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, compatibilityReportService.GenerateCallCount);
        Assert.Equal(viewModel.FanStatus, compatibilityReportService.LastFanStatus);
        Assert.Equal(1, compatibilityReportDialogService.ShowCallCount);
        Assert.Equal(
            compatibilityReportService.Report,
            compatibilityReportDialogService.LastReport);
        Assert.Equal(fanReadCountAfterRefresh, fanControlService.ReadStatusCallCount);
        Assert.Equal(0, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Equal(0, powerManagementService.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task ReportCompatibilityIssueCommand_CannotExecuteWhileRefreshIsBusy()
    {
        var hardwareDetectionService = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        var compatibilityReportService = new FakeCompatibilityReportService();
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            powerManagementService,
            compatibilityReportService: compatibilityReportService);
        var refreshDetectGate = new AsyncGate();
        hardwareDetectionService.QueueDetectGate(refreshDetectGate);

        viewModel.RefreshCommand.Execute(null);
        await refreshDetectGate.WaitUntilEnteredAsync();

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.ReportCompatibilityIssueCommand.CanExecute(null));

        viewModel.ReportCompatibilityIssueCommand.Execute(null);

        Assert.Equal(0, compatibilityReportService.GenerateCallCount);

        refreshDetectGate.Release();
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

        Assert.Equal(2, hardwareDetectionService.DetectCallCount);
        Assert.Equal(0, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Equal(1, powerManagementService.RestoreOriginalSettingsCallCount);

        restoreUiReadGate.Release();
        await WaitForIdleAsync(viewModel);
    }

    [Fact]
    public async Task Startup_NoMarkerGamingCpuSnapshotAndAppleAuto_ShowsTruthfulPartialGamingState()
    {
        var restoreStore = new InMemoryRestoreSnapshotStore();
        var originalSnapshot = InitialPowerState();
        await restoreStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore);
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(GamingOptimisedPowerState()),
            restoreSnapshotStore: restoreStore,
            fanControlService: new FakeFanControlService(VerifiedFanStatus()),
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(GamingOptimisedSessionState.PartialCpuOnly, viewModel.GamingOptimisedState);
        Assert.Equal(
            "Gaming CPU settings are active. Fans are currently using Apple Auto.",
            viewModel.DetectedProfileState);
        Assert.True(GetProfile(viewModel, "restore").IsEnabled);
        Assert.Contains("Re-enable Maximum Safe RPM", GetProfile(viewModel, "gaming-optimised").ToolTip);
        Assert.Equal(0, sessionFactory.OpenCallCount);
    }

    [Fact]
    public async Task PartialGaming_GamingButtonResumesVerifiedFansOnlyAndPreservesOriginalSnapshot()
    {
        var originalSnapshot = InitialPowerState();
        var restoreStore = new InMemoryRestoreSnapshotStore();
        await restoreStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);
        var power = new FakePowerManagementService(
            GamingOptimisedPowerState(),
            GamingOptimisedPowerState());
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore);
        var fanService = new FakeFanControlService(
            VerifiedFanStatus(),
            MaximumSafeRpmFanStatus());
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            power,
            restoreSnapshotStore: restoreStore,
            fanControlService: fanService,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(GamingOptimisedSessionState.Full, viewModel.GamingOptimisedState);
        Assert.NotNull(ownershipStore.Marker);
        Assert.Equal(
            originalSnapshot,
            await restoreStore.GetOriginalRestoreSnapshotAsync(CancellationToken.None));
        Assert.Equal(0, power.GuardedApplyCallCount);
        Assert.Equal(0, power.UnguardedApplyCallCount);
        Assert.Contains("without changing CPU settings", viewModel.StatusMessage);
    }

    [Fact]
    public async Task PartialGaming_AppleSmcStoppedDoesNotWriteAndRemainsTruthfulPartial()
    {
        var originalSnapshot = InitialPowerState();
        var restoreStore = new InMemoryRestoreSnapshotStore();
        await restoreStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);
        var power = new FakePowerManagementService(
            GamingOptimisedPowerState(),
            GamingOptimisedPowerState());
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore)
        {
            OpenSessionHandler = () => throw new AppleSmcServiceStateException(
                AppleSmcServiceState.Stopped)
        };
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            power,
            restoreSnapshotStore: restoreStore,
            fanControlService: new FakeFanControlService(
                InstalledStoppedFanStatus(),
                InstalledStoppedFanStatus()),
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(GamingOptimisedSessionState.PartialCpuOnly, viewModel.GamingOptimisedState);
        Assert.Null(ownershipStore.Marker);
        Assert.Equal(0, power.GuardedApplyCallCount);
        Assert.Equal(0, power.UnguardedApplyCallCount);
        Assert.Contains("Enable fan monitoring/control", viewModel.StatusMessage);
        Assert.Contains("BCPC fan override is not active", viewModel.DetectedProfileState);
    }

    [Fact]
    public async Task PartialGaming_MaxVerificationFailureRetainsOwnershipAndNeverReportsFull()
    {
        var originalSnapshot = InitialPowerState();
        var restoreStore = new InMemoryRestoreSnapshotStore();
        await restoreStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);
        var power = new FakePowerManagementService(
            GamingOptimisedPowerState(),
            GamingOptimisedPowerState());
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore)
        {
            OpenSessionHandler = () => Task.FromResult<IFanExecutionSession>(
                new TestFanExecutionSession(
                    overrideCoordinator: new TestFanOverrideCoordinator(ownershipStore)
                    {
                        ApplyHandler = async (model, capability, cancellationToken) =>
                        {
                            await ownershipStore.SaveNewAsync(
                                new FanOverrideOwnershipMarker(
                                    model,
                                    5321.25f,
                                    4789.5f,
                                    DateTimeOffset.UtcNow),
                                cancellationToken);
                            throw new InvalidOperationException(
                                "Maximum-safe fan override could not be verified by readback.");
                        }
                    }))
        };
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            power,
            restoreSnapshotStore: restoreStore,
            fanControlService: new FakeFanControlService(
                VerifiedFanStatus(),
                VerifiedFanStatus()),
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(
            GamingOptimisedSessionState.FanRecoveryPendingOrUnsafe,
            viewModel.GamingOptimisedState);
        Assert.NotNull(ownershipStore.Marker);
        Assert.Equal(
            originalSnapshot,
            await restoreStore.GetOriginalRestoreSnapshotAsync(CancellationToken.None));
        Assert.Equal(0, power.GuardedApplyCallCount);
        Assert.Equal(0, power.UnguardedApplyCallCount);
        Assert.DoesNotContain("fully active", viewModel.DetectedProfileState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Restore remains available", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Restore_FromPartialGaming_UsesExactOriginalSnapshotAndClearsItAfterSuccess()
    {
        var originalSnapshot = InitialPowerState();
        var restoreStore = new InMemoryRestoreSnapshotStore();
        await restoreStore.TrySaveOriginalRestoreSnapshotAsync(
            originalSnapshot,
            CancellationToken.None);
        var power = new FakePowerManagementService(
            GamingOptimisedPowerState(),
            originalSnapshot)
        {
            RestoreResult = SuccessfulRestoreOperation(
                GamingOptimisedPowerState(),
                ProcessorPowerSettings.FromSnapshot(originalSnapshot))
        };
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            power,
            restoreSnapshotStore: restoreStore,
            fanControlService: new FakeFanControlService(
                VerifiedFanStatus(),
                VerifiedFanStatus()),
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);
        Assert.Equal(GamingOptimisedSessionState.PartialCpuOnly, viewModel.GamingOptimisedState);

        GetProfile(viewModel, "restore").Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(originalSnapshot, power.SnapshotUsedForRestore);
        Assert.False(restoreStore.HasOriginalRestoreSnapshot);
        Assert.Equal(1, power.RestoreOriginalSettingsCallCount);
        Assert.Equal(GamingOptimisedSessionState.NoActiveSession, viewModel.GamingOptimisedState);
    }

    [Fact]
    public async Task StartupRecovery_NoMarker_DoesNotOpenFanExecutionSession()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var sessionFactory = new TestFanExecutionSessionFactory();
        var ownershipStore = new TestFanOverrideOwnershipStore { Marker = null };
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(0, sessionFactory.OpenCallCount);
        Assert.Equal(FanRecoveryState.None, viewModel.RecoveryState);
        Assert.Equal("No pending fan recovery.", viewModel.FanRecoveryStatus);
    }

    [Fact]
    public async Task StartupRecovery_MatchingMarkerAndManualFans_RecoversAppleAutoAndClearsMarker()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
                overrideCoordinator: new TestFanOverrideCoordinator
                {
                    RecoverHandler = (m, cap, ct) =>
                    {
                        ownershipStore.Marker = null;
                        return Task.FromResult(new FanOverrideRecoveryDecision(
                            FanOverrideRecoveryAction.RestoreAppleAuto,
                            "Restored to Apple Auto."));
                    }
                }))
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.Equal(FanRecoveryState.None, viewModel.RecoveryState);
        Assert.Equal("No pending fan recovery.", viewModel.FanRecoveryStatus);
        Assert.Null(ownershipStore.Marker);
    }

    [Fact]
    public async Task StartupRecovery_MatchingMarkerAndFansAlreadyAuto_StaleMarkerClearedWithoutWrite()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
                overrideCoordinator: new TestFanOverrideCoordinator
                {
                    RecoverHandler = (m, cap, ct) =>
                    {
                        ownershipStore.Marker = null;
                        return Task.FromResult(new FanOverrideRecoveryDecision(
                            FanOverrideRecoveryAction.None,
                            "Fans already in Apple Auto. Stale marker cleared."));
                    }
                }))
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.Equal(FanRecoveryState.None, viewModel.RecoveryState);
        Assert.Equal("No pending fan recovery.", viewModel.FanRecoveryStatus);
        Assert.Null(ownershipStore.Marker);
    }

    [Fact]
    public async Task StartupRecovery_MarkerPresentAndAppleSmcStopped_LeavesMarkerIntactAndPending()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var elevation = new FakeAppleSmcBackendElevationLauncher();
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => throw new AppleSmcServiceStateException(AppleSmcServiceState.Stopped)
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            elevationLauncher: elevation,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(0, elevation.LaunchCallCount);
        Assert.NotNull(ownershipStore.Marker);
        Assert.Equal(FanRecoveryState.PreviousSessionRecoveryPending, viewModel.RecoveryState);
        Assert.Equal("Previous fan override detected. Recovery to Apple Auto is pending.", viewModel.FanRecoveryStatus);
    }

    [Fact]
    public async Task StartupRecovery_MismatchedMarkerModel_RetainsMarkerAndBlocksRecovery()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                "MacBookPro15,1",
                5000f,
                4500f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory();
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(0, sessionFactory.OpenCallCount);
        Assert.NotNull(ownershipStore.Marker);
        Assert.Equal(FanRecoveryState.RecoveryBlocked, viewModel.RecoveryState);
        Assert.Equal("Fan recovery is blocked because current hardware state does not match the ownership marker.", viewModel.FanRecoveryStatus);
    }

    [Fact]
    public async Task StartupRecovery_UnsupportedOrUnverifiedHardware_RetainsMarkerAndBlocksRecovery()
    {
        var hardware = new FakeHardwareDetectionService(UnverifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory();
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(0, sessionFactory.OpenCallCount);
        Assert.NotNull(ownershipStore.Marker);
        Assert.Equal(FanRecoveryState.RecoveryBlocked, viewModel.RecoveryState);
    }

    [Fact]
    public async Task StartupRecovery_MarkerReadException_SetsInspectionFailedAndRetainsMarker()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            LoadException = new IOException("Disk failure")
        };
        var sessionFactory = new TestFanExecutionSessionFactory();
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(0, sessionFactory.OpenCallCount);
        Assert.Equal(FanRecoveryState.InspectionFailed, viewModel.RecoveryState);
        Assert.Equal("Inspection failed. Could not verify fan ownership marker.", viewModel.FanRecoveryStatus);
    }

    [Fact]
    public async Task StartupRecovery_NeverInvokesPowerRestore()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory();
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task StartupRecovery_OneShot_SubsequentRefreshDoesNotRecoverCurrentProcessOverride()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var initialPower = InitialPowerState();
        var gamingPower = GamingOptimisedPowerState();
        var power = new FakePowerManagementService(
            SuccessfulPowerOperation(initialPower, new ProcessorPowerSettings(95, 95, 0, 0)),
            initialPower,
            gamingPower);
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore);
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var gamingCommand = GetProfile(viewModel, "gaming-optimised").Command!;
        gamingCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanRecoveryState.CurrentSessionOverrideActive, viewModel.RecoveryState);

        var openCountBefore = sessionFactory.OpenCallCount;
        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(openCountBefore, sessionFactory.OpenCallCount);
        Assert.Equal(FanRecoveryState.CurrentSessionOverrideActive, viewModel.RecoveryState);
    }

    [Fact]
    public async Task Refresh_FirstStartupRecoveryCanceled_NextRefreshRetriesStartupRecovery()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var firstLoad = true;
        ownershipStore.LoadHandler = ct =>
        {
            if (firstLoad)
            {
                firstLoad = false;
                throw new OperationCanceledException("First refresh was canceled during startup recovery check.");
            }

            return Task.FromResult<FanOverrideOwnershipMarker?>(ownershipStore.Marker);
        };
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore);
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        // First refresh: canceled during startup recovery check
        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        // Marker must still be present on disk and recovery not yet evaluated
        Assert.NotNull(ownershipStore.Marker);
        Assert.Equal(0, sessionFactory.OpenCallCount);

        // Second refresh: retries startup recovery, succeeds and clears marker
        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.Null(ownershipStore.Marker);
        Assert.Equal(FanRecoveryState.None, viewModel.RecoveryState);
        Assert.Equal("No pending fan recovery.", viewModel.FanRecoveryStatus);
    }

    [Fact]
    public async Task GamingApply_SetsCurrentSessionOverrideActive()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var initialPower = InitialPowerState();
        var gamingPower = GamingOptimisedPowerState();
        var power = new FakePowerManagementService(
            SuccessfulPowerOperation(initialPower, new ProcessorPowerSettings(95, 95, 0, 0)),
            initialPower,
            gamingPower);
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore);
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var gamingCommand = GetProfile(viewModel, "gaming-optimised").Command!;
        gamingCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanRecoveryState.CurrentSessionOverrideActive, viewModel.RecoveryState);
        Assert.Equal("Gaming Optimised fan override is active. Restore returns fans to Apple Auto.", viewModel.FanRecoveryStatus);
        Assert.NotNull(ownershipStore.Marker);
        Assert.Equal(VerifiedHardwareModels.MacBookPro16_1, ownershipStore.Marker.Model);
    }

    [Fact]
    public async Task GamingApply_SuccessfulApplyWithMarkerUnexpectedlyMissing_SetsRecoveryBlocked()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var initialPower = InitialPowerState();
        var gamingPower = GamingOptimisedPowerState();
        var power = new FakePowerManagementService(
            SuccessfulPowerOperation(initialPower, new ProcessorPowerSettings(95, 95, 0, 0)),
            initialPower,
            gamingPower);
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
                overrideCoordinator: new TestFanOverrideCoordinator
                {
                    ApplyHandler = (m, cap, ct) => Task.FromResult(FanOverrideExecutionResult.Applied(
                        new FanOverrideOwnershipMarker(m, 5321.25f, 4789.5f, DateTimeOffset.UtcNow)))
                }))
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var gamingCommand = GetProfile(viewModel, "gaming-optimised").Command!;
        gamingCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanRecoveryState.RecoveryBlocked, viewModel.RecoveryState);
        Assert.Contains("blocked", viewModel.FanRecoveryStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(GetProfile(viewModel, "gaming-optimised").IsEnabled);
        Assert.True(GetProfile(viewModel, "restore").IsEnabled);
    }

    [Fact]
    public async Task GamingApply_SuccessfulApplyWithMismatchedMarker_SetsRecoveryBlocked()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var initialPower = InitialPowerState();
        var gamingPower = GamingOptimisedPowerState();
        var power = new FakePowerManagementService(
            SuccessfulPowerOperation(initialPower, new ProcessorPowerSettings(95, 95, 0, 0)),
            initialPower,
            gamingPower);
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
                overrideCoordinator: new TestFanOverrideCoordinator
                {
                    ApplyHandler = (m, cap, ct) =>
                    {
                        ownershipStore.Marker = new FanOverrideOwnershipMarker(
                            "MacBookPro15,1",
                            5000f,
                            4500f,
                            DateTimeOffset.UtcNow);
                        return Task.FromResult(FanOverrideExecutionResult.Applied(ownershipStore.Marker));
                    }
                }))
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var gamingCommand = GetProfile(viewModel, "gaming-optimised").Command!;
        gamingCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanRecoveryState.RecoveryBlocked, viewModel.RecoveryState);
        Assert.Contains("blocked", viewModel.FanRecoveryStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(GetProfile(viewModel, "gaming-optimised").IsEnabled);
        Assert.True(GetProfile(viewModel, "restore").IsEnabled);
    }

    [Fact]
    public async Task GamingApply_SuccessfulApplyWithMarkerReadFailure_SetsInspectionFailed()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var initialPower = InitialPowerState();
        var gamingPower = GamingOptimisedPowerState();
        var power = new FakePowerManagementService(
            SuccessfulPowerOperation(initialPower, new ProcessorPowerSettings(95, 95, 0, 0)),
            initialPower,
            gamingPower);
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
                overrideCoordinator: new TestFanOverrideCoordinator
                {
                    ApplyHandler = (m, cap, ct) =>
                    {
                        ownershipStore.LoadException = new IOException("Disk failure reading marker.");
                        return Task.FromResult(FanOverrideExecutionResult.Applied(
                            new FanOverrideOwnershipMarker(m, 5321.25f, 4789.5f, DateTimeOffset.UtcNow)));
                    }
                }))
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var gamingCommand = GetProfile(viewModel, "gaming-optimised").Command!;
        gamingCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanRecoveryState.InspectionFailed, viewModel.RecoveryState);
        Assert.Contains("Inspection failed", viewModel.FanRecoveryStatus, StringComparison.OrdinalIgnoreCase);
        Assert.False(GetProfile(viewModel, "gaming-optimised").IsEnabled);
        Assert.True(GetProfile(viewModel, "restore").IsEnabled);
    }

    [Fact]
    public async Task CurrentSessionOverrideActive_DisablesNextGamingApplyOnMBP16_1()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var initialPower = InitialPowerState();
        var gamingPower = GamingOptimisedPowerState();
        var power = new FakePowerManagementService(
            SuccessfulPowerOperation(initialPower, new ProcessorPowerSettings(95, 95, 0, 0)),
            initialPower,
            gamingPower);
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore);
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var gamingProfile = GetProfile(viewModel, "gaming-optimised");
        Assert.True(gamingProfile.IsEnabled);

        gamingProfile.Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        var gamingProfileAfter = GetProfile(viewModel, "gaming-optimised");
        Assert.False(gamingProfileAfter.IsEnabled);
        Assert.Equal("Restore the previous fan override before applying Gaming Optimised again.", gamingProfileAfter.ToolTip);
    }

    [Fact]
    public async Task CurrentSessionOverrideActive_EnablesRestore()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var initialPower = InitialPowerState();
        var gamingPower = GamingOptimisedPowerState();
        var power = new FakePowerManagementService(
            SuccessfulPowerOperation(initialPower, new ProcessorPowerSettings(95, 95, 0, 0)),
            initialPower,
            gamingPower);
        var restoreStore = new InMemoryRestoreSnapshotStore();
        var ownershipStore = new TestFanOverrideOwnershipStore();
        var sessionFactory = new TestFanExecutionSessionFactory(ownershipStore);
        var viewModel = CreateViewModel(
            hardware,
            power,
            restoreSnapshotStore: restoreStore,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var restoreBefore = GetProfile(viewModel, "restore");
        Assert.False(restoreBefore.IsEnabled);

        var gamingProfile = GetProfile(viewModel, "gaming-optimised");
        gamingProfile.Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        var restoreAfter = GetProfile(viewModel, "restore");
        Assert.True(restoreAfter.IsEnabled);
    }

    [Fact]
    public async Task Restore_EnabledWithFanMarkerAndNoPowerSnapshot()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var restoreStore = new InMemoryRestoreSnapshotStore();
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => throw new AppleSmcServiceStateException(AppleSmcServiceState.Stopped)
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            restoreSnapshotStore: restoreStore,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var restoreProfile = GetProfile(viewModel, "restore");
        Assert.True(restoreProfile.IsEnabled);
        Assert.Equal("Available - fan override recovery can be performed.", viewModel.RestoreSnapshotStatus);
    }

    [Fact]
    public async Task Restore_WithFanMarkerAndNoPowerSnapshot_PerformsFanOnlyRecovery()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var restoreStore = new InMemoryRestoreSnapshotStore();
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionOpenAttempt = 0;
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () =>
            {
                sessionOpenAttempt++;
                if (sessionOpenAttempt == 1)
                {
                    // Startup check: AppleSMC stopped, pending recovery
                    throw new AppleSmcServiceStateException(AppleSmcServiceState.Stopped);
                }

                return Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
                    overrideCoordinator: new TestFanOverrideCoordinator
                    {
                        RecoverHandler = (m, cap, ct) =>
                        {
                            ownershipStore.Marker = null;
                            return Task.FromResult(new FanOverrideRecoveryDecision(
                                FanOverrideRecoveryAction.RestoreAppleAuto,
                                "Restored to Apple Auto."));
                        }
                    }));
            }
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            restoreSnapshotStore: restoreStore,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var restoreProfile = GetProfile(viewModel, "restore");
        Assert.True(restoreProfile.IsEnabled);

        restoreProfile.Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
        Assert.Contains("No original processor snapshot required restoration", viewModel.StatusMessage);
        Assert.Equal(FanRecoveryState.None, viewModel.RecoveryState);
        Assert.Equal("No pending fan recovery.", viewModel.FanRecoveryStatus);
    }

    [Fact]
    public async Task Restore_WithFanMarkerAndPowerSnapshot_PerformsFansThenPower()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var initialPower = InitialPowerState();
        var power = new FakePowerManagementService(initialPower)
        {
            RestoreResult = SuccessfulRestoreOperation(initialPower, ProcessorPowerSettings.FromSnapshot(initialPower))
        };
        var restoreStore = new InMemoryRestoreSnapshotStore();
        await restoreStore.TrySaveOriginalRestoreSnapshotAsync(initialPower, CancellationToken.None);
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionOpenAttempt = 0;
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () =>
            {
                sessionOpenAttempt++;
                if (sessionOpenAttempt == 1)
                {
                    // Startup check: AppleSMC stopped, pending recovery
                    throw new AppleSmcServiceStateException(AppleSmcServiceState.Stopped);
                }

                return Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
                    overrideCoordinator: new TestFanOverrideCoordinator
                    {
                        RecoverHandler = (m, cap, ct) =>
                        {
                            ownershipStore.Marker = null;
                            return Task.FromResult(new FanOverrideRecoveryDecision(
                                FanOverrideRecoveryAction.RestoreAppleAuto,
                                "Restored to Apple Auto."));
                        }
                    }));
            }
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            restoreSnapshotStore: restoreStore,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var restoreProfile = GetProfile(viewModel, "restore");
        restoreProfile.Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(2, sessionOpenAttempt);
        Assert.Equal(1, power.RestoreOriginalSettingsCallCount);
        Assert.Equal(FanRecoveryState.None, viewModel.RecoveryState);
        Assert.Null(ownershipStore.Marker);
    }

    [Fact]
    public async Task Restore_WithoutFanMarkerAndWithPowerSnapshot_VerifiesFanBaselineThenPower()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var initialPower = InitialPowerState();
        var power = new FakePowerManagementService(initialPower)
        {
            RestoreResult = SuccessfulRestoreOperation(initialPower, ProcessorPowerSettings.FromSnapshot(initialPower))
        };
        var restoreStore = new InMemoryRestoreSnapshotStore();
        await restoreStore.TrySaveOriginalRestoreSnapshotAsync(initialPower, CancellationToken.None);
        var ownershipStore = new TestFanOverrideOwnershipStore { Marker = null };
        var sessionFactory = new TestFanExecutionSessionFactory();
        var viewModel = CreateViewModel(
            hardware,
            power,
            restoreSnapshotStore: restoreStore,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var restoreProfile = GetProfile(viewModel, "restore");
        restoreProfile.Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(1, sessionFactory.OpenCallCount);
        Assert.Equal(1, power.RestoreOriginalSettingsCallCount);
    }

    [Fact]
    public async Task Restore_MismatchedMarker_BlocksRestore()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var restoreStore = new InMemoryRestoreSnapshotStore();
        await restoreStore.TrySaveOriginalRestoreSnapshotAsync(InitialPowerState(), CancellationToken.None);
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                "MacBookPro15,1",
                5000f,
                4500f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory();
        var viewModel = CreateViewModel(
            hardware,
            power,
            restoreSnapshotStore: restoreStore,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var restoreProfile = GetProfile(viewModel, "restore");
        restoreProfile.Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(0, sessionFactory.OpenCallCount);
        Assert.Equal(0, power.RestoreOriginalSettingsCallCount);
        Assert.Contains("Restore failed", viewModel.StatusMessage);
        Assert.Equal(FanRecoveryState.RecoveryBlocked, viewModel.RecoveryState);
        Assert.NotNull(ownershipStore.Marker);
    }

    [Fact]
    public async Task Restore_Successful_ReloadsOwnershipStateAndObservesAbsent()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var initialPower = InitialPowerState();
        var power = new FakePowerManagementService(initialPower)
        {
            RestoreResult = SuccessfulRestoreOperation(initialPower, ProcessorPowerSettings.FromSnapshot(initialPower))
        };
        var restoreStore = new InMemoryRestoreSnapshotStore();
        await restoreStore.TrySaveOriginalRestoreSnapshotAsync(initialPower, CancellationToken.None);
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
                overrideCoordinator: new TestFanOverrideCoordinator
                {
                    RecoverHandler = (m, cap, ct) =>
                    {
                        ownershipStore.Marker = null;
                        return Task.FromResult(new FanOverrideRecoveryDecision(
                            FanOverrideRecoveryAction.RestoreAppleAuto,
                            "Restored."));
                    }
                }))
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            restoreSnapshotStore: restoreStore,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var loadCallsBefore = ownershipStore.LoadCallCount;

        var restoreProfile = GetProfile(viewModel, "restore");
        restoreProfile.Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.True(ownershipStore.LoadCallCount > loadCallsBefore);
        Assert.Equal(FanRecoveryState.None, viewModel.RecoveryState);
        Assert.Equal("No pending fan recovery.", viewModel.FanRecoveryStatus);
    }

    [Fact]
    public async Task Restore_LateCancellationAfterSuccessfulRestore_StillInspectsOwnershipState()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var initialPower = InitialPowerState();
        var power = new FakePowerManagementService(initialPower)
        {
            RestoreResult = SuccessfulRestoreOperation(initialPower, ProcessorPowerSettings.FromSnapshot(initialPower))
        };
        var restoreStore = new InMemoryRestoreSnapshotStore();
        await restoreStore.TrySaveOriginalRestoreSnapshotAsync(initialPower, CancellationToken.None);
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
                overrideCoordinator: new TestFanOverrideCoordinator
                {
                    RecoverHandler = (m, cap, ct) =>
                    {
                        ownershipStore.Marker = null;
                        return Task.FromResult(new FanOverrideRecoveryDecision(
                            FanOverrideRecoveryAction.RestoreAppleAuto,
                            "Restored."));
                    }
                }))
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            restoreSnapshotStore: restoreStore,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var restoreProfile = GetProfile(viewModel, "restore");
        restoreProfile.Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanRecoveryState.None, viewModel.RecoveryState);
        Assert.Equal("No pending fan recovery.", viewModel.FanRecoveryStatus);
        Assert.Contains(CancellationToken.None, ownershipStore.LoadTokens);
    }

    [Fact]
    public void RestoreButton_Tooltip_AccuratelyReflectsContext()
    {
        var profileCatalog = new ProfileCatalog();
        var restoreProfile = profileCatalog.GetProfiles(VerifiedMacBookPro16_1())
            .First(p => string.Equals(p.Id, "restore", StringComparison.OrdinalIgnoreCase));

        // 1. Fan marker only
        var fanOnlyVm = new ProfileButtonViewModel(
            restoreProfile,
            command: null,
            isRestoreSnapshotAvailable: false,
            isPowerStateReadable: true,
            hasFanRecoveryContext: true,
            isExactVerifiedMacBookPro16_1: true);
        Assert.True(fanOnlyVm.IsEnabled);
        Assert.Equal("Restore fan control to Apple Auto.", fanOnlyVm.ToolTip);

        // 2. Fan marker + CPU snapshot
        var fanAndCpuVm = new ProfileButtonViewModel(
            restoreProfile,
            command: null,
            isRestoreSnapshotAvailable: true,
            isPowerStateReadable: true,
            hasFanRecoveryContext: true,
            isExactVerifiedMacBookPro16_1: true);
        Assert.True(fanAndCpuVm.IsEnabled);
        Assert.Equal("Restore fan control to Apple Auto and restore the exact original saved power state.", fanAndCpuVm.ToolTip);

        // 3. CPU snapshot only
        var cpuOnlyVm = new ProfileButtonViewModel(
            restoreProfile,
            command: null,
            isRestoreSnapshotAvailable: true,
            isPowerStateReadable: true,
            hasFanRecoveryContext: false,
            isExactVerifiedMacBookPro16_1: true);
        Assert.True(cpuOnlyVm.IsEnabled);
        Assert.Equal("Restore the exact original saved power state.", cpuOnlyVm.ToolTip);
    }

    [Fact]
    public async Task Restore_Failed_RetainsMarkerDerivedState()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var restoreStore = new InMemoryRestoreSnapshotStore();
        await restoreStore.TrySaveOriginalRestoreSnapshotAsync(InitialPowerState(), CancellationToken.None);
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
                overrideCoordinator: new TestFanOverrideCoordinator
                {
                    RecoverHandler = (m, cap, ct) => Task.FromResult(new FanOverrideRecoveryDecision(
                        FanOverrideRecoveryAction.Blocked,
                        "Hardware readback check failed."))
                }))
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            restoreSnapshotStore: restoreStore,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var restoreProfile = GetProfile(viewModel, "restore");
        restoreProfile.Command!.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Contains("Restore failed", viewModel.StatusMessage);
        Assert.NotEqual(FanRecoveryState.None, viewModel.RecoveryState);
        Assert.NotNull(ownershipStore.Marker);
    }

    [Fact]
    public async Task EnableFanMonitoring_AfterStoppedAppleSmc_RetriesPendingRecoveryUnderGate()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var fanService = new FakeFanControlService(InstalledStoppedFanStatus(), VerifiedFanStatus());
        var elevation = new FakeAppleSmcBackendElevationLauncher();
        elevation.QueueResult(CompletedElevationResult());
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionOpenAttempt = 0;
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () =>
            {
                sessionOpenAttempt++;
                if (sessionOpenAttempt == 1)
                {
                    throw new AppleSmcServiceStateException(AppleSmcServiceState.Stopped);
                }

                return Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
                    overrideCoordinator: new TestFanOverrideCoordinator
                    {
                        RecoverHandler = (m, cap, ct) =>
                        {
                            ownershipStore.Marker = null;
                            return Task.FromResult(new FanOverrideRecoveryDecision(
                                FanOverrideRecoveryAction.RestoreAppleAuto,
                                "Restored Apple Auto."));
                        }
                    }));
            }
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            fanControlService: fanService,
            elevationLauncher: elevation,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanRecoveryState.PreviousSessionRecoveryPending, viewModel.RecoveryState);
        Assert.True(viewModel.IsFanMonitoringActivationAvailable);

        viewModel.EnableFanMonitoringCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanRecoveryState.None, viewModel.RecoveryState);
        Assert.Equal("No pending fan recovery.", viewModel.FanRecoveryStatus);
        Assert.Contains("Previous fan override was restored to Apple Auto", viewModel.StatusMessage);
        Assert.Null(ownershipStore.Marker);
    }

    [Fact]
    public async Task StartupRecovery_SessionCleanupException_LoggedAndHandled()
    {
        var hardware = new FakeHardwareDetectionService(VerifiedMacBookPro16_1());
        var power = new FakePowerManagementService(InitialPowerState());
        var logger = new TestApplicationLogger();
        var ownershipStore = new TestFanOverrideOwnershipStore
        {
            Marker = new FanOverrideOwnershipMarker(
                VerifiedHardwareModels.MacBookPro16_1,
                5321.25f,
                4789.5f,
                DateTimeOffset.UtcNow)
        };
        var sessionFactory = new TestFanExecutionSessionFactory
        {
            OpenSessionHandler = () => Task.FromResult<IFanExecutionSession>(new TestFanExecutionSession(
                overrideCoordinator: new TestFanOverrideCoordinator
                {
                    RecoverHandler = (m, cap, ct) => throw new InvalidOperationException("Recovery probe error")
                })
            {
                DisposeHandler = () => throw new InvalidOperationException("Session dispose error")
            })
        };
        var viewModel = CreateViewModel(
            hardware,
            power,
            logger: logger,
            fanExecutionSessionFactory: sessionFactory,
            ownershipStore: ownershipStore);

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.Equal(FanRecoveryState.RecoveryBlocked, viewModel.RecoveryState);
        Assert.NotEmpty(logger.Errors);
    }

    private static MainViewModel CreateViewModel(
        FakeHardwareDetectionService hardwareDetectionService,
        FakePowerManagementService powerManagementService,
        IRestoreSnapshotStore? restoreSnapshotStore = null,
        TestApplicationLogger? logger = null,
        FakeDiagnosticReportService? diagnosticReportService = null,
        FakeDiagnosticReportFileSaveService? diagnosticReportFileSaveService = null,
        FakeCompatibilityReportService? compatibilityReportService = null,
        FakeCompatibilityReportDialogService? compatibilityReportDialogService = null,
        IUserConfirmationService? userConfirmationService = null,
        FakeFanControlService? fanControlService = null,
        FakeAppleSmcBackendElevationLauncher? elevationLauncher = null,
        IApplicationOptionsService? applicationOptionsService = null,
        Func<TimeSpan, CancellationToken, Task>? fanPollingDelayAsync = null,
        IFanExecutionSessionFactory? fanExecutionSessionFactory = null,
        TestFanOverrideOwnershipStore? ownershipStore = null,
        GamingOptimisedRestoreCoordinator? gamingOptimisedRestoreCoordinator = null,
        GamingOptimisedFanResumeService? gamingOptimisedFanResumeService = null,
        ProfileRestoreService? profileRestoreService = null)
    {
        var profileCatalog = new ProfileCatalog();
        var profileExecutionResolver = new ProfileExecutionResolver();
        var fanProfileExecutionResolver = new FanProfileExecutionResolver();
        restoreSnapshotStore ??= new InMemoryRestoreSnapshotStore();
        powerManagementService.RestoreSnapshotStore = restoreSnapshotStore;
        ownershipStore ??= new TestFanOverrideOwnershipStore();
        fanExecutionSessionFactory ??= new TestFanExecutionSessionFactory(ownershipStore);

        var gamingOptimisedApplyCoordinator = new GamingOptimisedApplyCoordinator(
            profileExecutionResolver,
            fanProfileExecutionResolver,
            powerManagementService,
            fanExecutionSessionFactory);
        gamingOptimisedRestoreCoordinator ??= new GamingOptimisedRestoreCoordinator(
            powerManagementService,
            fanExecutionSessionFactory);
        gamingOptimisedFanResumeService ??= new GamingOptimisedFanResumeService(
            hardwareDetectionService,
            profileCatalog,
            profileExecutionResolver,
            fanProfileExecutionResolver,
            powerManagementService,
            restoreSnapshotStore,
            fanExecutionSessionFactory);

        var profileApplyService = new ProfileApplyService(
            hardwareDetectionService,
            profileCatalog,
            profileExecutionResolver,
            powerManagementService,
            gamingOptimisedApplyCoordinator);
        profileRestoreService ??= new ProfileRestoreService(
            hardwareDetectionService,
            powerManagementService,
            gamingOptimisedRestoreCoordinator,
            restoreSnapshotStore,
            ownershipStore,
            logger ?? new TestApplicationLogger());

        return new MainViewModel(
            hardwareDetectionService,
            powerManagementService,
            fanControlService ?? new FakeFanControlService(),
            elevationLauncher ?? new FakeAppleSmcBackendElevationLauncher(),
            applicationOptionsService ?? new FakeApplicationOptionsService(),
            profileCatalog,
            profileApplyService,
            restoreSnapshotStore,
            new ProcessorProfileStateEvaluator(
                profileCatalog,
                profileExecutionResolver),
            diagnosticReportService ?? new FakeDiagnosticReportService(),
            diagnosticReportFileSaveService ?? new FakeDiagnosticReportFileSaveService(),
            compatibilityReportService ?? new FakeCompatibilityReportService(),
            compatibilityReportDialogService ?? new FakeCompatibilityReportDialogService(),
            logger ?? new TestApplicationLogger(),
            userConfirmationService,
            fanPollingInterval: TimeSpan.FromSeconds(2),
            fanPollingDelayAsync: fanPollingDelayAsync,
            profileRestoreService: profileRestoreService,
            ownershipReader: ownershipStore,
            gamingOptimisedRestoreCoordinator: gamingOptimisedRestoreCoordinator,
            gamingOptimisedFanResumeService: gamingOptimisedFanResumeService);
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!condition() && !timeout.IsCancellationRequested)
        {
            await Task.Delay(10, timeout.Token);
        }

        Assert.True(condition());
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

    private static FanControlStatus VerifiedFanStatus(
        float fan0ActualRpm = 1840f,
        float fan1ActualRpm = 1691f)
    {
        return new FanControlStatus(
            FanBackendState.Running,
            FanSafetyState.ReadOnlyVerified,
            [
                new FanChannelReading(0, new FanReading(fan0ActualRpm, 5616f, FanOperatingMode.AppleAuto)),
                new FanChannelReading(1, new FanReading(fan1ActualRpm, 5200f, FanOperatingMode.AppleAuto))
            ],
            "Verified in test.");
    }

    private static FanControlStatus MaximumSafeRpmFanStatus()
    {
        return new FanControlStatus(
            FanBackendState.Running,
            FanSafetyState.ReadOnlyVerified,
            [
                new FanChannelReading(0, new FanReading(5616f, 5616f, FanOperatingMode.Manual)),
                new FanChannelReading(1, new FanReading(5200f, 5200f, FanOperatingMode.Manual))
            ],
            "Verified Maximum Safe RPM in test.",
            FanWriteControlState.MaximumSafeRpmDetected);
    }

    private static FanControlStatus InstalledStoppedFanStatus(
        string details = "AppleSMC is installed but stopped in the test.")
    {
        return FanControlStatus.CreateUnavailable(
            FanBackendState.InstalledStopped,
            FanSafetyState.MonitoringUnavailable,
            details);
    }

    private static AppleSmcBackendElevationResult CompletedElevationResult(
        AppleSmcBackendActivationOutcome helperOutcome = AppleSmcBackendActivationOutcome.Running,
        int exitCode = 0)
    {
        return new AppleSmcBackendElevationResult(
            AppleSmcBackendElevationOutcome.Completed,
            helperOutcome,
            exitCode,
            Exception: null);
    }

    private static AppleSmcBackendElevationResult UserCanceledElevationResult()
    {
        return new AppleSmcBackendElevationResult(
            AppleSmcBackendElevationOutcome.UserCanceled,
            HelperOutcome: null,
            ExitCode: null,
            Exception: null);
    }

    private static AppleSmcBackendElevationResult FailedElevationResult(
        string message,
        Exception? exception = null)
    {
        return new AppleSmcBackendElevationResult(
            AppleSmcBackendElevationOutcome.Failed,
            HelperOutcome: null,
            ExitCode: null,
            exception ?? new InvalidOperationException(message));
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

    private sealed class FakeCompatibilityReportService : ICompatibilityReportService
    {
        private readonly Queue<AsyncGate> _generateGates = [];

        public CompatibilityReportResult Report { get; set; } = new(
            "Compatibility report content.",
            "BootCampPerformanceControl-Compatibility-Test-0.3.0-rc.1.txt");

        public int GenerateCallCount { get; private set; }

        public FanControlStatus? LastFanStatus { get; private set; }

        public void QueueGenerateGate(AsyncGate gate)
        {
            _generateGates.Enqueue(gate);
        }

        public async Task<CompatibilityReportResult> GenerateAsync(
            FanControlStatus currentFanStatus,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenerateCallCount++;
            LastFanStatus = currentFanStatus;

            if (_generateGates.Count > 0)
            {
                await _generateGates.Dequeue().WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Report;
        }
    }

    private sealed class FakeCompatibilityReportDialogService : ICompatibilityReportDialogService
    {
        public int ShowCallCount { get; private set; }

        public CompatibilityReportResult? LastReport { get; private set; }

        public void Show(CompatibilityReportResult report)
        {
            ShowCallCount++;
            LastReport = report;
        }
    }

    private sealed class FakeFanControlService : IFanControlService
    {
        private readonly Queue<FanControlStatus> _statuses;
        private readonly Queue<Exception> _exceptions = [];
        private readonly Queue<AsyncGate> _readGates = [];
        private FanControlStatus _lastStatus;
        private int _activeReadCount;

        public FakeFanControlService(params FanControlStatus[] statuses)
        {
            _statuses = new Queue<FanControlStatus>(statuses);
            _lastStatus = statuses.Length == 0
                ? FanControlStatus.CreateUnavailable(
                    FanBackendState.NotApplicable,
                    FanSafetyState.MonitoringUnavailable,
                    "Unavailable in tests.")
                : statuses[^1];
        }

        public int ReadStatusCallCount { get; private set; }

        public int MaximumConcurrentReadCount { get; private set; }

        public List<string> ReadModels { get; } = [];

        public void QueueReadException(Exception exception)
        {
            _exceptions.Enqueue(exception);
        }

        public void QueueReadGate(AsyncGate gate)
        {
            _readGates.Enqueue(gate);
        }

        public async Task<FanControlStatus> ReadStatusAsync(
            string model,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadStatusCallCount++;
            ReadModels.Add(model);
            _activeReadCount++;
            MaximumConcurrentReadCount = Math.Max(
                MaximumConcurrentReadCount,
                _activeReadCount);

            try
            {
                if (_readGates.Count > 0)
                {
                    await _readGates.Dequeue().WaitAsync(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (_exceptions.Count > 0)
                {
                    throw _exceptions.Dequeue();
                }

                if (_statuses.Count > 0)
                {
                    _lastStatus = _statuses.Dequeue();
                }

                return _lastStatus;
            }
            finally
            {
                _activeReadCount--;
            }
        }
    }

    private sealed class FakeAppleSmcBackendElevationLauncher
        : IAppleSmcBackendElevationLauncher
    {
        private readonly Queue<AppleSmcBackendElevationResult> _results = [];
        private readonly Queue<AsyncGate> _launchGates = [];

        public int LaunchCallCount { get; private set; }

        public bool CancellationObserved { get; private set; }

        public void QueueResult(AppleSmcBackendElevationResult result)
        {
            _results.Enqueue(result);
        }

        public void QueueLaunchGate(AsyncGate gate)
        {
            _launchGates.Enqueue(gate);
        }

        public async Task<AppleSmcBackendElevationResult> LaunchAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LaunchCallCount++;

            try
            {
                if (_launchGates.Count > 0)
                {
                    await _launchGates.Dequeue().WaitAsync(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return _results.Count > 0
                    ? _results.Dequeue()
                    : FailedElevationResult("No elevation result was queued in the test.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
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

        public Exception? DetectException { get; set; }

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

            if (DetectException is not null)
            {
                throw DetectException;
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

        public PowerStateSnapshot? SnapshotUsedForRestore { get; private set; }

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
                SnapshotUsedForRestore = await RestoreSnapshotStore
                    .GetOriginalRestoreSnapshotAsync(cancellationToken);
                await RestoreSnapshotStore.ClearOriginalRestoreSnapshotAsync(cancellationToken);
            }

            return RestoreResult;
        }
    }

    private sealed class AsyncGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
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

    private sealed class ManualFanPollingDelay
    {
        private readonly object _sync = new();
        private readonly Queue<TaskCompletionSource> _pendingTicks = [];
        private int _requestCount;

        public int RequestCount
        {
            get
            {
                lock (_sync)
                {
                    return _requestCount;
                }
            }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Assert.Equal(TimeSpan.FromSeconds(2), delay);
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_sync)
            {
                _requestCount++;
                _pendingTicks.Enqueue(completion);
            }

            return WaitForTickAsync(completion, cancellationToken);
        }

        public void Advance()
        {
            TaskCompletionSource completion;

            lock (_sync)
            {
                completion = _pendingTicks.Dequeue();
            }

            completion.TrySetResult();
        }

        private static async Task WaitForTickAsync(
            TaskCompletionSource completion,
            CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));
            await completion.Task;
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

    private sealed class FakeApplicationOptionsService : IApplicationOptionsService
    {
        public ApplicationOptionsSnapshot Options { get; init; } =
            ApplicationOptionsSnapshot.Default;

        public Exception? SetException { get; init; }

        public int LoadCallCount { get; private set; }

        public int SetCloseBehaviorCallCount { get; private set; }

        public int SetStartWithWindowsCallCount { get; private set; }

        public int SetStartMinimizedToTrayCallCount { get; private set; }

        public ApplicationCloseBehavior? LastCloseBehavior { get; private set; }

        public bool? LastStartWithWindows { get; private set; }

        public bool? LastStartMinimizedToTray { get; private set; }

        public ApplicationOptionsSnapshot Load()
        {
            LoadCallCount++;
            return Options;
        }

        public void SetCloseBehavior(ApplicationCloseBehavior closeBehavior)
        {
            SetCloseBehaviorCallCount++;

            if (SetException is not null)
            {
                throw SetException;
            }

            LastCloseBehavior = closeBehavior;
        }

        public void SetStartWithWindows(bool enabled)
        {
            SetStartWithWindowsCallCount++;

            if (SetException is not null)
            {
                throw SetException;
            }

            LastStartWithWindows = enabled;
        }

        public void SetStartMinimizedToTray(bool enabled)
        {
            SetStartMinimizedToTrayCallCount++;

            if (SetException is not null)
            {
                throw SetException;
            }

            LastStartMinimizedToTray = enabled;
        }
    }
}
