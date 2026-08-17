using System.Reflection;
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
    public void ProfileButtons_GamingOptimisedIsDisabledForUnverifiedHardware()
    {
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(UnverifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()));

        viewModel.RefreshCommand.Execute(null);

        var gaming = GetProfile(viewModel, "gaming-optimised");
        Assert.False(gaming.IsEnabled);
        Assert.Null(gaming.Command);
        Assert.Contains("verified compatible", gaming.ToolTip, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("balanced")]
    [InlineData("full-performance")]
    public void ProfileButtons_NonGamingProfilesRemainDisabled(string profileId)
    {
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            new FakePowerManagementService(InitialPowerState()));

        viewModel.RefreshCommand.Execute(null);

        var profile = GetProfile(viewModel, profileId);
        Assert.False(profile.IsEnabled);
        Assert.Null(profile.Command);
        Assert.Contains("not yet connected", profile.ToolTip, StringComparison.OrdinalIgnoreCase);
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
    }

    [Fact]
    public void GamingButton_ReverifiesThroughProfileApplyServiceBeforeAnyWrite()
    {
        var hardwareDetectionService = new FakeHardwareDetectionService(
            VerifiedMacBookPro16_1(),
            UnverifiedMacBookPro16_1());
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
    public void GamingButton_SuccessfulApplyUiRereadCancellationDoesNotReportCanceledApply()
    {
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
            logger: logger);

        viewModel.RefreshCommand.Execute(null);
        GetProfile(viewModel, "gaming-optimised").Command!.Execute(null);

        Assert.Contains("was applied and verified", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refreshing the displayed power state failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("canceled", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed. Check the log", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, powerManagementService.GuardedApplyCallCount);
        Assert.Equal(0, powerManagementService.UnguardedApplyCallCount);
        Assert.Equal(0, powerManagementService.RestoreOriginalSettingsCallCount);
        Assert.Single(logger.Errors);
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
        Assert.Contains("restored successfully", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(restoreSnapshotStore.HasOriginalRestoreSnapshot);
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
        var powerReadGate = new AsyncGate();
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        powerManagementService.QueueReadGate(powerReadGate);
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService);

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
        var powerReadGate = new AsyncGate();
        var powerManagementService = new FakePowerManagementService(InitialPowerState());
        powerManagementService.QueueReadGate(powerReadGate);
        var viewModel = CreateViewModel(
            new FakeHardwareDetectionService(VerifiedMacBookPro16_1()),
            powerManagementService);

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
        TestApplicationLogger? logger = null)
    {
        var profileCatalog = new ProfileCatalog();
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
                new ProfileExecutionResolver(),
                powerManagementService),
            restoreSnapshotStore,
            logger ?? new TestApplicationLogger());
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
            IsApple: true,
            IsVerified: true,
            HardwareVerificationStatus.Verified,
            "Verified.");
    }

    private static ModelVerificationResult UnverifiedMacBookPro16_1()
    {
        return new ModelVerificationResult(
            "Apple Inc.",
            VerifiedHardwareModels.MacBookPro16_1,
            IsApple: true,
            IsVerified: false,
            HardwareVerificationStatus.UnverifiedAppleModel,
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

            if (_applyResult.IsSuccessful && RestoreSnapshotStore is not null)
            {
                await RestoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
                    expectedStateBefore,
                    cancellationToken);
            }

            return _applyResult;
        }

        public async Task<PowerOperationResult> RestoreOriginalSettingsAsync(CancellationToken cancellationToken)
        {
            RestoreOriginalSettingsCallCount++;

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
}
