using System.Windows.Controls;
using System.Windows.Threading;
using BootCampPerformanceControl.ApplicationSettings;
using BootCampPerformanceControl.Diagnostics;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.BackendActivation;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;
using BootCampPerformanceControl.Tests.TestDoubles;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl.Tests.UI;

public sealed class MainViewModelThreadAffinityTests
{
    [Fact]
    public async Task GamingOptimised_PostApplyOwnershipReload_CompletesOnWpfDispatcher()
    {
        var completion = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));

            var scenario = RunGamingOptimisedScenarioAsync();
            _ = scenario.ContinueWith(
                completedTask =>
                {
                    var exception = completedTask.IsFaulted
                        ? completedTask.Exception?.GetBaseException()
                        : completedTask.IsCanceled
                            ? new TaskCanceledException(completedTask)
                            : null;

                    completion.TrySetResult(exception);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "BCPC S0 WPF dispatcher regression"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var exception = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(exception);
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    private static async Task RunGamingOptimisedScenarioAsync()
    {
        var restoreSnapshotStore = new InMemoryRestoreSnapshotStore();
        var ownershipStore = new TestFanOverrideOwnershipStore();

        ownershipStore.LoadHandler = _ =>
        {
            if (ownershipStore.LoadCallCount == 1)
            {
                return Task.FromResult(ownershipStore.Marker);
            }

            var completion = new TaskCompletionSource<FanOverrideOwnershipMarker?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            ThreadPool.QueueUserWorkItem(
                _ => completion.TrySetResult(ownershipStore.Marker));

            return completion.Task;
        };

        var hardwareDetectionService = new StubHardwareDetectionService();
        var powerManagementService = new StubPowerManagementService(
            restoreSnapshotStore,
            InitialPowerState(),
            InitialPowerState(),
            GamingOptimisedPowerState());
        var fanControlService = new StubFanControlService();
        var logger = new TestApplicationLogger();
        var viewModel = CreateViewModel(
            hardwareDetectionService,
            powerManagementService,
            fanControlService,
            restoreSnapshotStore,
            ownershipStore,
            logger);

        var refreshButton = new Button
        {
            Command = viewModel.RefreshCommand
        };

        Assert.True(refreshButton.Dispatcher.CheckAccess());

        viewModel.RefreshCommand.Execute(null);
        await WaitForIdleAsync(viewModel);

        var gamingProfile = Assert.Single(
            viewModel.ProfileButtons,
            profile => string.Equals(
                profile.ProfileId,
                "gaming-optimised",
                StringComparison.OrdinalIgnoreCase));

        Assert.True(gamingProfile.IsEnabled);
        Assert.NotNull(gamingProfile.Command);

        gamingProfile.Command.Execute(null);
        await WaitForIdleAsync(viewModel);

        Assert.True(refreshButton.Dispatcher.CheckAccess());
        Assert.True(refreshButton.IsEnabled);
        Assert.True(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.IsBusy);
        Assert.Equal(FanRecoveryState.CurrentSessionOverrideActive, viewModel.RecoveryState);
        Assert.Contains("applied successfully", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Profile application failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, ownershipStore.LoadCallCount);
        Assert.NotNull(ownershipStore.Marker);
        Assert.Equal(3, powerManagementService.ReadCurrentStateCallCount);
        Assert.Empty(logger.Errors);

        GC.KeepAlive(refreshButton);
    }

    private static MainViewModel CreateViewModel(
        IHardwareDetectionService hardwareDetectionService,
        StubPowerManagementService powerManagementService,
        IFanControlService fanControlService,
        IRestoreSnapshotStore restoreSnapshotStore,
        TestFanOverrideOwnershipStore ownershipStore,
        TestApplicationLogger logger)
    {
        var profileCatalog = new ProfileCatalog();
        var profileExecutionResolver = new ProfileExecutionResolver();
        var fanProfileExecutionResolver = new FanProfileExecutionResolver();
        var fanExecutionSessionFactory = new TestFanExecutionSessionFactory(ownershipStore);
        var gamingOptimisedApplyCoordinator = new GamingOptimisedApplyCoordinator(
            profileExecutionResolver,
            fanProfileExecutionResolver,
            powerManagementService,
            fanExecutionSessionFactory);
        var gamingOptimisedRestoreCoordinator = new GamingOptimisedRestoreCoordinator(
            powerManagementService,
            fanExecutionSessionFactory);
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

        return new MainViewModel(
            hardwareDetectionService,
            powerManagementService,
            fanControlService,
            new StubAppleSmcBackendElevationLauncher(),
            new StubApplicationOptionsService(),
            profileCatalog,
            profileApplyService,
            restoreSnapshotStore,
            new ProcessorProfileStateEvaluator(
                profileCatalog,
                profileExecutionResolver),
            new StubDiagnosticReportService(),
            new StubDiagnosticReportFileSaveService(),
            new StubCompatibilityReportService(),
            new StubCompatibilityReportDialogService(),
            logger,
            fanPollingInterval: TimeSpan.FromSeconds(2),
            profileRestoreService: profileRestoreService,
            ownershipReader: ownershipStore,
            gamingOptimisedRestoreCoordinator: gamingOptimisedRestoreCoordinator);
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
            "Verified in S0 dispatcher regression test.");
    }

    private static PowerStateSnapshot InitialPowerState()
    {
        return PowerState(
            processorMaximumAc: 100,
            processorMaximumDc: 100,
            boostModeAc: 2,
            boostModeDc: 2);
    }

    private static PowerStateSnapshot GamingOptimisedPowerState()
    {
        return PowerState(
            processorMaximumAc: 95,
            processorMaximumDc: 95,
            boostModeAc: 0,
            boostModeDc: 0);
    }

    private static PowerStateSnapshot PowerState(
        uint processorMaximumAc,
        uint processorMaximumDc,
        uint boostModeAc,
        uint boostModeDc)
    {
        return new PowerStateSnapshot(
            Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"),
            processorMaximumAc,
            processorMaximumDc,
            boostModeAc,
            boostModeDc,
            DateTimeOffset.Parse("2026-09-04T12:00:00+00:00"));
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

    private sealed class StubHardwareDetectionService : IHardwareDetectionService
    {
        private readonly ModelVerificationResult _verificationResult = VerifiedMacBookPro16_1();

        public Task<HardwareSnapshot> DetectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new HardwareSnapshot(
                new ComputerSystemInfo(
                    _verificationResult.Manufacturer,
                    _verificationResult.Model,
                    "x64-based PC"),
                Processor: null,
                VideoControllers: [],
                OperatingSystem: null,
                DateTimeOffset.Parse("2026-09-04T12:00:00+00:00")));
        }

        public ModelVerificationResult VerifyModel(HardwareSnapshot snapshot)
        {
            return _verificationResult;
        }
    }

    private sealed class StubPowerManagementService : IPowerManagementService
    {
        private readonly IRestoreSnapshotStore _restoreSnapshotStore;
        private readonly Queue<PowerStateSnapshot> _readStates;
        private PowerStateSnapshot _lastReadState;

        public StubPowerManagementService(
            IRestoreSnapshotStore restoreSnapshotStore,
            params PowerStateSnapshot[] readStates)
        {
            _restoreSnapshotStore = restoreSnapshotStore;
            _readStates = new Queue<PowerStateSnapshot>(readStates);
            _lastReadState = readStates[^1];
        }

        public int ReadCurrentStateCallCount { get; private set; }

        public Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCurrentStateCallCount++;

            if (_readStates.Count > 0)
            {
                _lastReadState = _readStates.Dequeue();
            }

            return Task.FromResult(_lastReadState);
        }

        public Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(SuccessfulPowerOperation(_lastReadState, requestedSettings));
        }

        public async Task<PowerOperationResult> ApplyProcessorSettingsAsync(
            ProcessorPowerSettings requestedSettings,
            PowerStateSnapshot expectedStateBefore,
            CancellationToken cancellationToken)
        {
            await _restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
                expectedStateBefore,
                cancellationToken);

            return SuccessfulPowerOperation(expectedStateBefore, requestedSettings);
        }

        public Task<PowerOperationResult> RestoreOriginalSettingsAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubFanControlService : IFanControlService
    {
        public Task<FanControlStatus> ReadStatusAsync(
            string model,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new FanControlStatus(
                FanBackendState.Running,
                FanSafetyState.ReadOnlyVerified,
                new FanReading(5616f, 5616f, FanOperatingMode.Manual),
                new FanReading(5200f, 5200f, FanOperatingMode.Manual),
                "Verified fan state in S0 dispatcher regression test.",
                FanWriteControlState.MaximumSafeRpmDetected));
        }
    }

    private sealed class StubAppleSmcBackendElevationLauncher
        : IAppleSmcBackendElevationLauncher
    {
        public Task<AppleSmcBackendElevationResult> LaunchAsync(
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubApplicationOptionsService : IApplicationOptionsService
    {
        public ApplicationOptionsSnapshot Load() => ApplicationOptionsSnapshot.Default;

        public void SetCloseBehavior(ApplicationCloseBehavior closeBehavior)
        {
            throw new NotSupportedException();
        }

        public void SetStartWithWindows(bool enabled)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubDiagnosticReportService : IDiagnosticReportService
    {
        public Task<DiagnosticReportResult> GenerateAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubDiagnosticReportFileSaveService : IDiagnosticReportFileSaveService
    {
        public Task<bool> SaveAsync(
            DiagnosticReportResult report,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubCompatibilityReportService : ICompatibilityReportService
    {
        public Task<CompatibilityReportResult> GenerateAsync(
            FanControlStatus currentFanStatus,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubCompatibilityReportDialogService : ICompatibilityReportDialogService
    {
        public void Show(CompatibilityReportResult report)
        {
            throw new NotSupportedException();
        }
    }
}
