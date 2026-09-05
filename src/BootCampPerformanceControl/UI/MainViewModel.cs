using System.Collections.ObjectModel;
using System.Windows.Input;
using BootCampPerformanceControl.ApplicationInfo;
using BootCampPerformanceControl.ApplicationSettings;
using BootCampPerformanceControl.Diagnostics;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.BackendActivation;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;
using StructuredFanControlStatus = BootCampPerformanceControl.FanControl.FanControlStatus;

namespace BootCampPerformanceControl.UI;

public sealed class MainViewModel : ViewModelBase
{
    private static readonly TimeSpan DefaultFanPollingInterval = TimeSpan.FromSeconds(2);
    private static readonly FanSafetyPolicy FanIdentitySafetyPolicy = new();

    private readonly IHardwareDetectionService _hardwareDetectionService;
    private readonly IPowerManagementService _powerManagementService;
    private readonly IFanControlService _fanControlService;
    private readonly IAppleSmcBackendElevationLauncher _appleSmcBackendElevationLauncher;
    private readonly IApplicationOptionsService _applicationOptionsService;
    private readonly IProfileCatalog _profileCatalog;
    private readonly ProfileApplyService _profileApplyService;
    private readonly ProfileRestoreService _profileRestoreService;
    private readonly IRestoreSnapshotStore _restoreSnapshotStore;
    private readonly IFanOverrideOwnershipReader _ownershipReader;
    private readonly GamingOptimisedRestoreCoordinator? _gamingOptimisedRestoreCoordinator;
    private readonly GamingOptimisedFanResumeService? _gamingOptimisedFanResumeService;
    private readonly ProcessorProfileStateEvaluator _processorProfileStateEvaluator;
    private readonly GamingOptimisedSessionStateEvaluator _gamingOptimisedSessionStateEvaluator = new();
    private readonly IDiagnosticReportService _diagnosticReportService;
    private readonly IDiagnosticReportFileSaveService _diagnosticReportFileSaveService;
    private readonly ICompatibilityReportService _compatibilityReportService;
    private readonly ICompatibilityReportDialogService _compatibilityReportDialogService;
    private readonly IApplicationLogger _logger;
    private readonly IUserConfirmationService _userConfirmationService;
    private readonly TimeSpan _fanPollingInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _fanPollingDelayAsync;
    private readonly SemaphoreSlim _fanOperationGate = new(1, 1);
    private readonly object _fanMonitoringSync = new();
    private readonly HashSet<string> _acknowledgedUntestedModels = new(StringComparer.OrdinalIgnoreCase);

    private ModelVerificationResult _lastVerificationResult = ModelVerificationResult.Unknown();
    private bool _lastPowerStateReadSucceeded;
    private string _macModel = "Not detected";
    private string _cpu = "Not detected";
    private string _coreThreadCount = "Not detected";
    private string _gpu = "Not detected";
    private string _windowsVersion = "Not detected";
    private string _platformSupport = "Not checked";
    private string _modelValidation = "Not checked";
    private string _compatibilityDetails = "Not checked";
    private string _activePowerScheme = "Not read";
    private string _processorMaximumAc = "Not read";
    private string _processorMaximumDc = "Not read";
    private string _boostModeAc = "Not read";
    private string _boostModeDc = "Not read";
    private string _detectedProfileState = "Unknown - power state has not been read.";
    private ProcessorProfileState _processorProfileState = ProcessorProfileState.Unknown;
    private GamingOptimisedSessionState _gamingOptimisedSessionState = GamingOptimisedSessionState.Unknown;
    private string _restoreSnapshotStatus = "Not available.";
    private FanRecoveryState _fanRecoveryState = FanRecoveryState.None;
    private string _fanRecoveryStatus = "No pending fan recovery.";
    private bool _startupRecoveryEvaluated;
    private StructuredFanControlStatus _fanStatus = StructuredFanControlStatus.NotChecked;
    private string _statusMessage = "Ready";
    private bool _isBusy;
    private ApplicationCloseBehavior _closeBehavior =
        ApplicationOptionsSnapshot.Default.CloseBehavior;
    private bool _startWithWindows;
    private bool _startMinimizedToTray =
        ApplicationOptionsSnapshot.Default.StartMinimizedToTray;
    private string? _fanMonitoringModel;
    private FanBackendState? _lastLoggedFanBackendState;
    private FanSafetyState? _lastLoggedFanSafetyState;
    private CancellationTokenSource? _fanMonitoringCancellationSource;
    private Task? _fanMonitoringTask;
    private Task? _fanMonitoringStopTask;

    public MainViewModel(
        IHardwareDetectionService hardwareDetectionService,
        IPowerManagementService powerManagementService,
        IFanControlService fanControlService,
        IAppleSmcBackendElevationLauncher appleSmcBackendElevationLauncher,
        IApplicationOptionsService applicationOptionsService,
        IProfileCatalog profileCatalog,
        ProfileApplyService profileApplyService,
        IRestoreSnapshotStore restoreSnapshotStore,
        ProcessorProfileStateEvaluator processorProfileStateEvaluator,
        IDiagnosticReportService diagnosticReportService,
        IDiagnosticReportFileSaveService diagnosticReportFileSaveService,
        ICompatibilityReportService compatibilityReportService,
        ICompatibilityReportDialogService compatibilityReportDialogService,
        IApplicationLogger logger,
        IUserConfirmationService? userConfirmationService = null,
        TimeSpan? fanPollingInterval = null,
        Func<TimeSpan, CancellationToken, Task>? fanPollingDelayAsync = null,
        ProfileRestoreService? profileRestoreService = null)
        : this(
            hardwareDetectionService,
            powerManagementService,
            fanControlService,
            appleSmcBackendElevationLauncher,
            applicationOptionsService,
            profileCatalog,
            profileApplyService,
            restoreSnapshotStore,
            processorProfileStateEvaluator,
            diagnosticReportService,
            diagnosticReportFileSaveService,
            compatibilityReportService,
            compatibilityReportDialogService,
            logger,
            userConfirmationService,
            fanPollingInterval,
            fanPollingDelayAsync,
            profileRestoreService,
            ownershipReader: null,
            gamingOptimisedRestoreCoordinator: null,
            gamingOptimisedFanResumeService: null)
    {
    }

    internal MainViewModel(
        IHardwareDetectionService hardwareDetectionService,
        IPowerManagementService powerManagementService,
        IFanControlService fanControlService,
        IAppleSmcBackendElevationLauncher appleSmcBackendElevationLauncher,
        IApplicationOptionsService applicationOptionsService,
        IProfileCatalog profileCatalog,
        ProfileApplyService profileApplyService,
        IRestoreSnapshotStore restoreSnapshotStore,
        ProcessorProfileStateEvaluator processorProfileStateEvaluator,
        IDiagnosticReportService diagnosticReportService,
        IDiagnosticReportFileSaveService diagnosticReportFileSaveService,
        ICompatibilityReportService compatibilityReportService,
        ICompatibilityReportDialogService compatibilityReportDialogService,
        IApplicationLogger logger,
        IUserConfirmationService? userConfirmationService = null,
        TimeSpan? fanPollingInterval = null,
        Func<TimeSpan, CancellationToken, Task>? fanPollingDelayAsync = null,
        ProfileRestoreService? profileRestoreService = null,
        IFanOverrideOwnershipReader? ownershipReader = null,
        GamingOptimisedRestoreCoordinator? gamingOptimisedRestoreCoordinator = null,
        GamingOptimisedFanResumeService? gamingOptimisedFanResumeService = null)
    {
        ArgumentNullException.ThrowIfNull(hardwareDetectionService);
        ArgumentNullException.ThrowIfNull(powerManagementService);
        ArgumentNullException.ThrowIfNull(fanControlService);
        ArgumentNullException.ThrowIfNull(appleSmcBackendElevationLauncher);
        ArgumentNullException.ThrowIfNull(applicationOptionsService);
        ArgumentNullException.ThrowIfNull(profileCatalog);
        ArgumentNullException.ThrowIfNull(profileApplyService);
        ArgumentNullException.ThrowIfNull(restoreSnapshotStore);
        ArgumentNullException.ThrowIfNull(processorProfileStateEvaluator);
        ArgumentNullException.ThrowIfNull(diagnosticReportService);
        ArgumentNullException.ThrowIfNull(diagnosticReportFileSaveService);
        ArgumentNullException.ThrowIfNull(compatibilityReportService);
        ArgumentNullException.ThrowIfNull(compatibilityReportDialogService);
        ArgumentNullException.ThrowIfNull(logger);

        _hardwareDetectionService = hardwareDetectionService;
        _powerManagementService = powerManagementService;
        _fanControlService = fanControlService;
        _appleSmcBackendElevationLauncher = appleSmcBackendElevationLauncher;
        _applicationOptionsService = applicationOptionsService;
        _profileCatalog = profileCatalog;
        _profileApplyService = profileApplyService;
        _restoreSnapshotStore = restoreSnapshotStore;
        _ownershipReader = ownershipReader ?? new JsonFanOverrideOwnershipStore(logger);
        _gamingOptimisedRestoreCoordinator = gamingOptimisedRestoreCoordinator;
        _gamingOptimisedFanResumeService = gamingOptimisedFanResumeService;
        _profileRestoreService = profileRestoreService
            ?? new ProfileRestoreService(
                hardwareDetectionService,
                powerManagementService,
                _gamingOptimisedRestoreCoordinator,
                restoreSnapshotStore,
                _ownershipReader,
                logger);
        _processorProfileStateEvaluator = processorProfileStateEvaluator;
        _diagnosticReportService = diagnosticReportService;
        _diagnosticReportFileSaveService = diagnosticReportFileSaveService;
        _compatibilityReportService = compatibilityReportService;
        _compatibilityReportDialogService = compatibilityReportDialogService;
        _logger = logger;
        _userConfirmationService = userConfirmationService ?? new WpfUserConfirmationService();
        LoadApplicationOptions();
        _fanPollingInterval = fanPollingInterval ?? DefaultFanPollingInterval;
        if (_fanPollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fanPollingInterval),
                "Fan polling interval must be greater than zero.");
        }

        _fanPollingDelayAsync = fanPollingDelayAsync ?? Task.Delay;
        RefreshRestoreSnapshotStatus();
        RefreshCommand = new AsyncCommand(
            RefreshAsync,
            canExecute: () => !IsBusy,
            onCanceled: OnRefreshCanceled,
            onException: OnRefreshException);
        EnableFanMonitoringCommand = new AsyncCommand(
            EnableFanMonitoringAsync,
            canExecute: () => !IsBusy && IsFanMonitoringActivationAvailable,
            onCanceled: OnEnableFanMonitoringCanceled,
            onException: OnEnableFanMonitoringException);
        ExportDiagnosticReportCommand = new AsyncCommand(
            ExportDiagnosticReportAsync,
            canExecute: () => !IsBusy,
            onCanceled: OnExportDiagnosticReportCanceled,
            onException: OnExportDiagnosticReportException);
        ReportCompatibilityIssueCommand = new AsyncCommand(
            ReportCompatibilityIssueAsync,
            canExecute: () => !IsBusy,
            onCanceled: OnReportCompatibilityIssueCanceled,
            onException: OnReportCompatibilityIssueException);
        UpdateProfiles(_lastVerificationResult, isPowerStateReadable: false);
    }

    public ICommand RefreshCommand { get; }

    public ICommand EnableFanMonitoringCommand { get; }

    public ICommand ExportDiagnosticReportCommand { get; }

    public ICommand ReportCompatibilityIssueCommand { get; }

    public ObservableCollection<ProfileButtonViewModel> ProfileButtons { get; } = [];

    public string ApplicationVersion { get; } = ApplicationVersionProvider.GetInformationalVersion();

    public string MacModel
    {
        get => _macModel;
        private set => SetProperty(ref _macModel, value);
    }

    public string Cpu
    {
        get => _cpu;
        private set => SetProperty(ref _cpu, value);
    }

    public string CoreThreadCount
    {
        get => _coreThreadCount;
        private set => SetProperty(ref _coreThreadCount, value);
    }

    public string Gpu
    {
        get => _gpu;
        private set => SetProperty(ref _gpu, value);
    }

    public string WindowsVersion
    {
        get => _windowsVersion;
        private set => SetProperty(ref _windowsVersion, value);
    }

    public string PlatformSupport
    {
        get => _platformSupport;
        private set => SetProperty(ref _platformSupport, value);
    }

    public string ModelValidation
    {
        get => _modelValidation;
        private set => SetProperty(ref _modelValidation, value);
    }

    public string CompatibilityDetails
    {
        get => _compatibilityDetails;
        private set => SetProperty(ref _compatibilityDetails, value);
    }

    public string ActivePowerScheme
    {
        get => _activePowerScheme;
        private set => SetProperty(ref _activePowerScheme, value);
    }

    public string ProcessorMaximumAc
    {
        get => _processorMaximumAc;
        private set => SetProperty(ref _processorMaximumAc, value);
    }

    public string ProcessorMaximumDc
    {
        get => _processorMaximumDc;
        private set => SetProperty(ref _processorMaximumDc, value);
    }

    public string BoostModeAc
    {
        get => _boostModeAc;
        private set => SetProperty(ref _boostModeAc, value);
    }

    public string BoostModeDc
    {
        get => _boostModeDc;
        private set => SetProperty(ref _boostModeDc, value);
    }

    public string DetectedProfileState
    {
        get => _detectedProfileState;
        private set => SetProperty(ref _detectedProfileState, value);
    }

    public string RestoreSnapshotStatus
    {
        get => _restoreSnapshotStatus;
        private set => SetProperty(ref _restoreSnapshotStatus, value);
    }

    public string FanRecoveryStatus
    {
        get => _fanRecoveryStatus;
        private set => SetProperty(ref _fanRecoveryStatus, value);
    }

    internal FanRecoveryState RecoveryState => _fanRecoveryState;

    internal GamingOptimisedSessionState GamingOptimisedState => _gamingOptimisedSessionState;

    private void SetFanRecoveryState(FanRecoveryState state, string statusMessage)
    {
        _fanRecoveryState = state;
        FanRecoveryStatus = statusMessage;
        OnPropertyChanged(nameof(RecoveryState));
        RefreshGamingOptimisedSessionState();
    }

    public StructuredFanControlStatus FanStatus
    {
        get => _fanStatus;
        private set
        {
            if (SetProperty(ref _fanStatus, value))
            {
                OnPropertyChanged(nameof(FanControlStatus));
                NotifyFanMonitoringActivationStateChanged();
            }
        }
    }

    public string FanControlStatus => FanStatus.DisplayText;

    public bool IsFanMonitoringActivationAvailable =>
        FanStatus.BackendState == FanBackendState.InstalledStopped
        && HasVerifiedFanActivationIdentity(_lastVerificationResult);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyOperationCommandsCanExecuteChanged();
            }
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _closeBehavior == ApplicationCloseBehavior.MinimizeToTray;
        set
        {
            if (value)
            {
                UpdateCloseBehavior(ApplicationCloseBehavior.MinimizeToTray);
            }
        }
    }

    public bool ExitApplicationOnClose
    {
        get => _closeBehavior == ApplicationCloseBehavior.ExitApplication;
        set
        {
            if (value)
            {
                UpdateCloseBehavior(ApplicationCloseBehavior.ExitApplication);
            }
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (value == _startWithWindows)
            {
                return;
            }

            try
            {
                _applicationOptionsService.SetStartWithWindows(value);
                _startWithWindows = value;
                OnPropertyChanged();
                StatusMessage = value
                    ? "BootCamp Performance Control will start when you sign in to Windows."
                    : "Windows startup disabled for BootCamp Performance Control.";
                _logger.Info($"Windows startup option changed. Enabled: {value}.");
            }
            catch (Exception exception)
            {
                OnPropertyChanged();
                StatusMessage = "Windows startup option could not be changed. Check the log for details.";
                _logger.Error("Updating the Windows startup option failed.", exception);
            }
        }
    }

    public bool StartMinimizedToTray
    {
        get => _startMinimizedToTray;
        set
        {
            if (value == _startMinimizedToTray)
            {
                return;
            }

            try
            {
                _applicationOptionsService.SetStartMinimizedToTray(value);
                _startMinimizedToTray = value;
                OnPropertyChanged();
                StatusMessage = value
                    ? "BootCamp Performance Control will start minimized to the system tray."
                    : "BootCamp Performance Control will show its main window when started.";
                _logger.Info($"Start-minimized-to-tray option changed. Enabled: {value}.");
            }
            catch (Exception exception)
            {
                OnPropertyChanged();
                StatusMessage = "Start-minimized-to-tray option could not be changed. Check the log for details.";
                _logger.Error("Updating the start-minimized-to-tray option failed.", exception);
            }
        }
    }

    public void StartFanMonitoring()
    {
        lock (_fanMonitoringSync)
        {
            if (_fanMonitoringTask is not null)
            {
                return;
            }

            _fanMonitoringCancellationSource = new CancellationTokenSource();
            _fanMonitoringTask = MonitorFansAsync(_fanMonitoringCancellationSource.Token);
        }

        _logger.Info($"Fan monitoring started. Polling interval: {_fanPollingInterval.TotalSeconds:0.###} seconds.");
    }

    public Task StopFanMonitoringAsync()
    {
        if (EnableFanMonitoringCommand is AsyncCommand enableFanMonitoringCommand)
        {
            enableFanMonitoringCommand.Cancel();
        }

        lock (_fanMonitoringSync)
        {
            if (_fanMonitoringTask is null || _fanMonitoringCancellationSource is null)
            {
                return Task.CompletedTask;
            }

            if (_fanMonitoringStopTask is null)
            {
                _fanMonitoringCancellationSource.Cancel();
                _fanMonitoringStopTask = StopFanMonitoringCoreAsync(
                    _fanMonitoringTask,
                    _fanMonitoringCancellationSource);
            }

            return _fanMonitoringStopTask;
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = "Refreshing...";
        var fanGateAcquired = false;

        try
        {
            await _fanOperationGate.WaitAsync(cancellationToken);
            fanGateAcquired = true;
            _fanMonitoringModel = null;

            var errors = 0;
            var verificationResult = ModelVerificationResult.Unknown();
            var hasUsableFanModelIdentity = false;
            var hardwareIdentityEstablished = false;

            try
            {
                _logger.Info("Hardware detection started.");
                var hardwareSnapshot = await _hardwareDetectionService.DetectAsync(cancellationToken);
                verificationResult = _hardwareDetectionService.VerifyModel(hardwareSnapshot);
                hardwareIdentityEstablished = true;
                SetLastVerificationResult(verificationResult);
                ApplyHardware(hardwareSnapshot);
                ApplyCompatibility(verificationResult);
                hasUsableFanModelIdentity = HasUsableFanModelIdentity(verificationResult);
                if (hasUsableFanModelIdentity)
                {
                    _fanMonitoringModel = verificationResult.Model;
                }

                _logger.Info(
                    $"Hardware detection completed. Detected Mac model: {verificationResult.Model}. Platform support: {verificationResult.PlatformSupport}. Validation level: {verificationResult.ValidationLevel}.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors++;
                ApplyHardwareFailure();
                ApplyCompatibility(verificationResult);
                SetLastVerificationResult(verificationResult);
                _logger.Error("Hardware detection failed.", exception);
            }

            if (!_startupRecoveryEvaluated)
            {
                await EvaluateStartupRecoveryUnderGateAsync(verificationResult, cancellationToken);
                _startupRecoveryEvaluated = true;
            }

            if (hasUsableFanModelIdentity)
            {
                try
                {
                    _logger.Info($"Fan read started. Model: {verificationResult.Model}.");
                    var fanStatus = await _fanControlService
                        .ReadStatusAsync(verificationResult.Model, cancellationToken);
                    ApplyFanStatus(fanStatus);

                    _logger.Info($"Fan read completed. Model: {verificationResult.Model}. Backend: {fanStatus.BackendState}. Safety: {fanStatus.SafetyState}.");
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    errors++;
                    ApplyFanStatus(StructuredFanControlStatus.CreateUnavailable(
                        FanBackendState.Error,
                        FanSafetyState.Error,
                        "The read failed unexpectedly. Check the log for details."));
                    _logger.Error(
                        $"Fan read failed. Model: {verificationResult.Model}.",
                        exception);
                }
            }
            else
            {
                ApplyFanStatus(StructuredFanControlStatus.CreateUnavailable(
                    hardwareIdentityEstablished
                        ? FanBackendState.NotApplicable
                        : FanBackendState.Unavailable,
                    hardwareIdentityEstablished
                        ? FanSafetyState.UnsupportedModel
                        : FanSafetyState.MonitoringUnavailable,
                    hardwareIdentityEstablished
                        ? "The detected model is not supported for fan monitoring."
                        : "A verified hardware model identity could not be established."));
                _logger.Info("Fan read skipped because hardware detection did not produce a usable supported Intel Mac model identity.");
            }

            try
            {
                _logger.Info("Power-state read started.");
                var currentPowerState = await _powerManagementService.ReadCurrentStateAsync(cancellationToken);
                _lastPowerStateReadSucceeded = true;
                ApplyPowerState(currentPowerState);
                ApplyDetectedProfileState(currentPowerState, verificationResult);
                RefreshRestoreSnapshotStatus();
                _logger.Info($"Power-state read completed. Active scheme: {currentPowerState.SchemeId}.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors++;
                _lastPowerStateReadSucceeded = false;
                ApplyPowerFailure();
                _logger.Error("Power-state read failed.", exception);
            }

            UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);

            StatusMessage = errors == 0
                ? "Refresh completed."
                : "Refresh completed with errors. Check the log for details.";
        }
        finally
        {
            if (fanGateAcquired)
            {
                _fanOperationGate.Release();
            }

            IsBusy = false;
        }
    }

    private async Task EnableFanMonitoringAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = "Requesting administrator permission to enable fan monitoring...";
        var fanGateAcquired = false;

        try
        {
            if (!IsFanMonitoringActivationAvailable)
            {
                StatusMessage = "Fan monitoring activation is not available for the current backend and hardware state.";
                return;
            }

            var verifiedModel = _lastVerificationResult.Model;
            _logger.Info($"AppleSMC backend activation requested. Model: {verifiedModel}.");

            await _fanOperationGate.WaitAsync(cancellationToken);
            fanGateAcquired = true;

            if (!IsFanMonitoringActivationAvailable
                || !string.Equals(
                    verifiedModel,
                    _lastVerificationResult.Model,
                    StringComparison.Ordinal))
            {
                _fanMonitoringModel = null;
                StatusMessage = "Fan monitoring activation is no longer available for the current hardware state.";
                _logger.Info("AppleSMC backend activation skipped because the verified hardware identity changed.");
                return;
            }

            var launchResult = await _appleSmcBackendElevationLauncher
                .LaunchAsync(cancellationToken);

            if (launchResult.Outcome == AppleSmcBackendElevationOutcome.UserCanceled)
            {
                StatusMessage = "Fan monitoring was not enabled.";
                _logger.Info("AppleSMC backend activation was canceled by the user at the UAC prompt.");
                return;
            }

            if (launchResult.Outcome == AppleSmcBackendElevationOutcome.Failed
                && !launchResult.ExitCode.HasValue)
            {
                StatusMessage = "Fan monitoring could not be enabled. Check the log for details.";
                _logger.Error(
                    "The elevated AppleSMC helper could not be launched.",
                    launchResult.Exception
                        ?? new InvalidOperationException("The elevation launcher did not provide failure details."));
                return;
            }

            if (launchResult.Outcome == AppleSmcBackendElevationOutcome.Failed)
            {
                _logger.Error(
                    $"Elevated AppleSMC helper completed with unrecognized exit code {FormatExitCode(launchResult.ExitCode)}. Parent verification will determine the backend state.",
                    launchResult.Exception
                        ?? new InvalidOperationException("The elevation launcher did not provide failure details."));
            }
            else
            {
                _logger.Info(
                    $"Elevated AppleSMC helper completed. Outcome: {FormatHelperOutcome(launchResult.HelperOutcome)}; exit code: {FormatExitCode(launchResult.ExitCode)}.");
            }

            if (!HasVerifiedFanActivationIdentity(_lastVerificationResult)
                || !string.Equals(
                    verifiedModel,
                    _lastVerificationResult.Model,
                    StringComparison.Ordinal))
            {
                _fanMonitoringModel = null;
                StatusMessage = "Fan monitoring was not enabled because the verified hardware identity is no longer valid.";
                _logger.Info("Parent verification skipped because the verified hardware identity changed after helper completion.");
                return;
            }

            var observedStatus = await _fanControlService
                .ReadStatusAsync(verifiedModel, cancellationToken);
            ApplyFanStatus(observedStatus);
            _fanMonitoringModel = verifiedModel;

            _logger.Info(
                $"Parent AppleSMC verification completed. Backend: {observedStatus.BackendState}. Safety: {observedStatus.SafetyState}.");

            if (observedStatus.IsAvailable)
            {
                if (_fanRecoveryState == FanRecoveryState.PreviousSessionRecoveryPending)
                {
                    await TryRetryPendingFanRecoveryUnderGateAsync(verifiedModel, cancellationToken);
                }
                else
                {
                    StatusMessage = "Fan monitoring enabled.";
                }
            }
            else
            {
                StatusMessage = $"Fan monitoring was not enabled. Parent verification observed backend '{observedStatus.BackendDisplayText}' and safety '{observedStatus.SafetyDisplayText}'. Helper outcome: '{FormatHelperOutcome(launchResult.HelperOutcome)}'.";
            }
        }
        finally
        {
            if (fanGateAcquired)
            {
                _fanOperationGate.Release();
            }

            IsBusy = false;
        }
    }

    private async Task MonitorFansAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _fanPollingDelayAsync(_fanPollingInterval, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (IsBusy || !await _fanOperationGate.WaitAsync(0, cancellationToken))
                {
                    continue;
                }

                try
                {
                    var model = _fanMonitoringModel;
                    if (IsBusy || string.IsNullOrWhiteSpace(model))
                    {
                        continue;
                    }

                    try
                    {
                        var status = await _fanControlService
                            .ReadStatusAsync(model, cancellationToken);
                        ApplyFanStatus(status);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        var changed = ApplyFanStatus(StructuredFanControlStatus.CreateUnavailable(
                            FanBackendState.Error,
                            FanSafetyState.Error,
                            "The live read failed unexpectedly. Check the log for details."));

                        if (changed)
                        {
                            _logger.Error(
                                $"Live fan read failed unexpectedly. Model: {model}.",
                                exception);
                        }
                    }
                }
                finally
                {
                    _fanOperationGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal monitor shutdown.
        }
        catch (Exception exception)
        {
            ApplyFanStatus(StructuredFanControlStatus.CreateUnavailable(
                FanBackendState.Error,
                FanSafetyState.Error,
                "Fan monitoring stopped after an unexpected scheduler failure."));
            _logger.Error("Fan monitoring stopped unexpectedly.", exception);
        }
    }

    private async Task StopFanMonitoringCoreAsync(
        Task monitoringTask,
        CancellationTokenSource cancellationSource)
    {
        await Task.Yield();

        try
        {
            await monitoringTask;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the expected shutdown path.
        }
        catch (Exception exception)
        {
            _logger.Error("Fan monitoring shutdown failed unexpectedly.", exception);
        }
        finally
        {
            lock (_fanMonitoringSync)
            {
                if (ReferenceEquals(_fanMonitoringTask, monitoringTask))
                {
                    _fanMonitoringTask = null;
                    _fanMonitoringCancellationSource = null;
                    _fanMonitoringStopTask = null;
                }
            }

            cancellationSource.Dispose();
        }

        _logger.Info("Fan monitoring stopped.");
    }

    private bool ApplyFanStatus(StructuredFanControlStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        var stateChanged = _lastLoggedFanBackendState != status.BackendState
            || _lastLoggedFanSafetyState != status.SafetyState;

        FanStatus = status;
        RefreshGamingOptimisedSessionState();

        if (stateChanged)
        {
            _lastLoggedFanBackendState = status.BackendState;
            _lastLoggedFanSafetyState = status.SafetyState;
            _logger.Info(
                $"Fan monitoring state changed. Backend: {status.BackendState}. Safety: {status.SafetyState}. {status.Details}");
        }

        return stateChanged;
    }

    private void OnRefreshCanceled(OperationCanceledException exception)
    {
        StatusMessage = "Refresh canceled.";
        _logger.Info($"Refresh canceled: {exception.Message}");
    }

    private void OnRefreshException(Exception exception)
    {
        StatusMessage = "Refresh failed. Check the log for details.";
        _logger.Error("Refresh failed unexpectedly.", exception);
    }

    private void OnEnableFanMonitoringCanceled(OperationCanceledException exception)
    {
        StatusMessage = "Fan monitoring activation canceled.";
        _logger.Info($"Fan monitoring activation canceled: {exception.Message}");
    }

    private void OnEnableFanMonitoringException(Exception exception)
    {
        StatusMessage = "Fan monitoring activation failed. Check the log for details.";
        _logger.Error("Fan monitoring activation failed unexpectedly.", exception);
    }

    private async Task ExportDiagnosticReportAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = "Generating diagnostic report...";

        try
        {
            _logger.Info("Diagnostic report export started.");
            var report = await _diagnosticReportService
                .GenerateAsync(cancellationToken);
            var saved = await _diagnosticReportFileSaveService
                .SaveAsync(report, cancellationToken);

            if (!saved)
            {
                StatusMessage = "Diagnostic report export canceled.";
                _logger.Info("Diagnostic report export canceled.");
                return;
            }

            StatusMessage = "Diagnostic report exported successfully.";
            _logger.Info("Diagnostic report exported successfully.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnExportDiagnosticReportCanceled(OperationCanceledException exception)
    {
        StatusMessage = "Diagnostic report export canceled.";
        _logger.Info("Diagnostic report export canceled.");
    }

    private void OnExportDiagnosticReportException(Exception exception)
    {
        StatusMessage = "Diagnostic report export failed. Check the log for details.";
        _logger.Error(
            "Diagnostic report export failed unexpectedly.",
            new InvalidOperationException(
                $"Diagnostic report export failed with {exception.GetType().Name}."));
    }

    private async Task ReportCompatibilityIssueAsync(CancellationToken cancellationToken)
    {
        CompatibilityReportResult report;

        IsBusy = true;
        StatusMessage = "Generating compatibility report...";

        try
        {
            _logger.Info("Compatibility report generation started.");
            report = await _compatibilityReportService
                .GenerateAsync(FanStatus, cancellationToken);
        }
        finally
        {
            IsBusy = false;
        }

        _compatibilityReportDialogService.Show(report);
        StatusMessage = "Compatibility report ready for review.";
        _logger.Info("Compatibility report opened for user review.");
    }

    private void OnReportCompatibilityIssueCanceled(OperationCanceledException exception)
    {
        StatusMessage = "Compatibility report generation canceled.";
        _logger.Info($"Compatibility report generation canceled: {exception.Message}");
    }

    private void OnReportCompatibilityIssueException(Exception exception)
    {
        StatusMessage = "Compatibility report could not be generated. Check the log for details.";
        _logger.Error("Compatibility report generation failed unexpectedly.", exception);
    }

    private async Task ApplyProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        var isPartialGamingFanResume = string.Equals(
                profileId,
                "gaming-optimised",
                StringComparison.OrdinalIgnoreCase)
            && _gamingOptimisedSessionState == GamingOptimisedSessionState.PartialCpuOnly
            && _lastVerificationResult.PlatformSupport == PlatformSupportStatus.SupportedIntelMac;

        if (string.Equals(profileId, "gaming-optimised", StringComparison.OrdinalIgnoreCase)
            && _lastVerificationResult.ValidationLevel == ModelValidationLevel.NotIndividuallyTested)
        {
            if (!_acknowledgedUntestedModels.Contains(_lastVerificationResult.Model))
            {
                var confirmed = _userConfirmationService.ConfirmUntestedModelApply(_lastVerificationResult.Model);
                if (!confirmed)
                {
                    StatusMessage = "Profile application canceled.";
                    _logger.Info($"Profile application canceled by user for untested model: {_lastVerificationResult.Model}.");
                    return;
                }

                _acknowledgedUntestedModels.Add(_lastVerificationResult.Model);
            }
        }

        IsBusy = true;
        StatusMessage = isPartialGamingFanResume
            ? "Re-enabling Maximum Safe RPM fans..."
            : $"Applying profile '{profileId}'...";
        var fanGateAcquired = false;

        try
        {
            await _fanOperationGate.WaitAsync(cancellationToken);
            fanGateAcquired = true;

            if (isPartialGamingFanResume)
            {
                await ResumeGamingOptimisedFansUnderGateAsync(cancellationToken);
                return;
            }

            _logger.Info($"Profile application started: {profileId}.");
            ProfileApplyResult result;
            try
            {
                result = await _profileApplyService.ApplyProfileAsync(profileId, cancellationToken);
            }
            catch
            {
                if (string.Equals(profileId, "gaming-optimised", StringComparison.OrdinalIgnoreCase))
                {
                    await InspectFanOwnershipAfterFanOperationFailureAsync();
                }

                throw;
            }

            if (!result.IsSuccessful)
            {
                if (string.Equals(profileId, "gaming-optimised", StringComparison.OrdinalIgnoreCase))
                {
                    await InspectFanOwnershipAfterFanOperationFailureAsync();
                }

                RefreshProfileStateAfterUncertainApply();
                StatusMessage = $"Profile application failed: {result.FailureReason}";
                _logger.Error(
                    $"Profile application failed for '{profileId}': {result.FailureReason}",
                    new InvalidOperationException(result.FailureReason));
                return;
            }

            _logger.Info($"Profile application succeeded: {profileId}. Re-reading power state.");
            SetLastVerificationResult(result.ModelVerificationResult);

            if (string.Equals(profileId, "gaming-optimised", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var marker = await _ownershipReader
                        .LoadAsync(CancellationToken.None);

                    if (marker is null)
                    {
                        if (result.IsFanOverrideActive)
                        {
                            SetFanRecoveryState(
                                FanRecoveryState.RecoveryBlocked,
                                "Fan recovery is blocked because Maximum Safe RPM was applied, but the ownership marker could not be verified on disk.");
                        }
                        else
                        {
                            SetFanRecoveryState(
                                FanRecoveryState.None,
                                "Gaming Optimised is active for the processor. No BCPC fan override is active.");
                        }
                    }
                    else if (!string.Equals(marker.Model, result.ModelVerificationResult.Model, StringComparison.Ordinal))
                    {
                        _logger.Error(
                            $"Gaming Optimised apply succeeded but persisted marker model '{marker.Model}' does not match verified hardware model '{result.ModelVerificationResult.Model}'.",
                            new InvalidOperationException("Ownership marker model mismatch after apply."));
                        SetFanRecoveryState(
                            FanRecoveryState.RecoveryBlocked,
                            "Fan recovery is blocked because current hardware state does not match the ownership marker.");
                    }
                    else if (result.IsFanOverrideActive)
                    {
                        SetFanRecoveryState(
                            FanRecoveryState.CurrentSessionOverrideActive,
                            "Gaming Optimised fan override is active. Restore returns fans to Apple Auto.");
                    }
                    else
                    {
                        SetFanRecoveryState(
                            FanRecoveryState.RecoveryBlocked,
                            "A fan ownership marker exists, but this apply did not establish a new verified fan override. Restore fan control before continuing.");
                    }
                }
                catch (Exception exception)
                {
                    _logger.Error("Reloading ownership marker after successful Gaming Optimised apply failed.", exception);
                    SetFanRecoveryState(
                        FanRecoveryState.InspectionFailed,
                        "Inspection failed. Could not verify fan ownership marker.");
                }
            }

            var profileDisplayName = GetProfileDisplayName(profileId);

            try
            {
                var currentPowerState = await _powerManagementService.ReadCurrentStateAsync(cancellationToken);
                _lastPowerStateReadSucceeded = true;
                ApplyPowerState(currentPowerState);
                ApplyDetectedProfileState(currentPowerState, _lastVerificationResult);
                RefreshRestoreSnapshotStatus();
                UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
                StatusMessage = $"Profile '{profileId}' applied successfully. Power state refreshed.";
                _logger.Info($"Power-state read completed after profile application. Active scheme: {currentPowerState.SchemeId}.");
            }
            catch (OperationCanceledException exception)
            {
                HandlePostSuccessPowerStateRefreshCanceled(
                    $"Profile '{profileDisplayName}' was applied and verified, but refreshing the displayed power state was canceled. Use Refresh to update the display.",
                    $"Power-state UI refresh canceled after successful profile application for '{profileId}'",
                    exception);
            }
            catch (Exception exception)
            {
                _lastPowerStateReadSucceeded = false;
                ApplyDetectedProfileState(ProcessorProfileState.Unknown);
                RefreshRestoreSnapshotStatus();
                UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
                StatusMessage = $"Profile '{profileId}' was applied and verified, but refreshing the displayed power state failed. Use Refresh to retry.";
                _logger.Error(
                    $"Power-state UI refresh failed after successful profile application for '{profileId}'.",
                    exception);
            }

            await TryRefreshFanStatusUnderGateAsync(result.ModelVerificationResult.Model, cancellationToken);
        }
        finally
        {
            if (fanGateAcquired)
            {
                _fanOperationGate.Release();
            }

            IsBusy = false;
        }
    }

    private async Task ResumeGamingOptimisedFansUnderGateAsync(
        CancellationToken cancellationToken)
    {
        if (_gamingOptimisedFanResumeService is null)
        {
            StatusMessage = "Maximum Safe RPM resume is unavailable because the fan-only coordinator is not configured.";
            return;
        }

        _logger.Info("Gaming Optimised fan-only Maximum Safe RPM resume started.");

        GamingOptimisedFanResumeResult result;
        try
        {
            result = await _gamingOptimisedFanResumeService.ResumeAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await InspectFanOwnershipAfterFanOperationFailureAsync();
            await TryRefreshFanStatusUnderGateAsync(_lastVerificationResult.Model, CancellationToken.None);
            throw;
        }
        catch (AppleSmcServiceStateException exception)
        {
            _logger.Info($"Gaming Optimised fan-only resume stopped because AppleSMC is unavailable: {exception.Message}");
            await InspectFanOwnershipAfterFanOperationFailureAsync();
            await TryRefreshFanStatusUnderGateAsync(_lastVerificationResult.Model, CancellationToken.None);
            UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
            StatusMessage = "Maximum Safe RPM was not re-enabled. Enable fan monitoring/control, then try again.";
            return;
        }
        catch (Exception exception)
        {
            _logger.Error("Gaming Optimised fan-only Maximum Safe RPM resume failed unexpectedly.", exception);
            await InspectFanOwnershipAfterFanOperationFailureAsync();
            await TryRefreshFanStatusUnderGateAsync(_lastVerificationResult.Model, CancellationToken.None);
            UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
            StatusMessage = "Maximum Safe RPM could not be re-enabled safely. Restore remains available; check the log for details.";
            return;
        }

        SetLastVerificationResult(result.ModelVerificationResult);

        if (!result.IsSuccessful)
        {
            await InspectFanOwnershipAfterFanOperationFailureAsync();
            await TryRefreshFanStatusUnderGateAsync(result.ModelVerificationResult.Model, cancellationToken);
            UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
            StatusMessage = $"Maximum Safe RPM was not re-enabled: {result.FailureReason}";
            _logger.Info($"Gaming Optimised fan-only resume was not completed: {result.FailureReason}");
            return;
        }

        try
        {
            var marker = await _ownershipReader.LoadAsync(CancellationToken.None);
            if (marker is null)
            {
                SetFanRecoveryState(
                    FanRecoveryState.RecoveryBlocked,
                    "Fan recovery is blocked because the ownership marker could not be verified on disk.");
            }
            else if (!string.Equals(
                         marker.Model,
                         result.ModelVerificationResult.Model,
                         StringComparison.Ordinal))
            {
                SetFanRecoveryState(
                    FanRecoveryState.RecoveryBlocked,
                    "Fan recovery is blocked because current hardware state does not match the ownership marker.");
            }
            else
            {
                SetFanRecoveryState(
                    FanRecoveryState.CurrentSessionOverrideActive,
                    "Gaming Optimised fan override is active. Restore returns fans to Apple Auto.");
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Reloading ownership marker after fan-only resume failed.", exception);
            SetFanRecoveryState(
                FanRecoveryState.InspectionFailed,
                "Inspection failed. Could not verify fan ownership marker.");
        }

        await TryRefreshFanStatusUnderGateAsync(result.ModelVerificationResult.Model, cancellationToken);

        if (_gamingOptimisedSessionState != GamingOptimisedSessionState.Full)
        {
            SetFanRecoveryState(
                FanRecoveryState.RecoveryBlocked,
                "Maximum Safe RPM ownership exists, but the live fan state could not be verified.");
            StatusMessage = "Maximum Safe RPM was written, but the live fan state could not be verified. Restore remains available.";
        }
        else
        {
            StatusMessage = "Gaming Optimised is fully active. Maximum Safe RPM was re-enabled and verified without changing CPU settings.";
            _logger.Info("Gaming Optimised fan-only Maximum Safe RPM resume completed and live readback was verified.");
        }

        UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
    }

    private async Task InspectFanOwnershipAfterFanOperationFailureAsync()
    {
        try
        {
            var marker = await _ownershipReader.LoadAsync(CancellationToken.None);
            if (marker is not null)
            {
                SetFanRecoveryState(
                    FanRecoveryState.RecoveryBlocked,
                    "A fan ownership marker remains after the failed resume. Restore fan control before continuing.");
            }
            else
            {
                SetFanRecoveryState(
                    FanRecoveryState.None,
                    "No pending fan recovery.");
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Inspecting fan ownership after failed fan-only resume failed.", exception);
            SetFanRecoveryState(
                FanRecoveryState.InspectionFailed,
                "Inspection failed. Could not verify fan ownership marker.");
        }
    }

    private async Task RestoreOriginalSettingsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = "Restoring original settings...";
        var fanGateAcquired = false;

        try
        {
            await _fanOperationGate.WaitAsync(cancellationToken);
            fanGateAcquired = true;

            _logger.Info("Restore started.");
            var result = await _profileRestoreService.RestoreAsync(cancellationToken);

            if (!result.IsSuccessful)
            {
                var failureMessage = result.FailureMessage;
                StatusMessage = $"Restore failed: {failureMessage}";
                UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
                _logger.Error(
                    $"Restore failed: {failureMessage}",
                    new InvalidOperationException(failureMessage));
                return;
            }

            _logger.Info("Restore succeeded.");
            SetLastVerificationResult(result.ModelVerificationResult);

            try
            {
                var markerAfterRestore = await _ownershipReader
                    .LoadAsync(CancellationToken.None);

                if (markerAfterRestore is not null)
                {
                    _logger.Error(
                        "Restore returned success but fan override ownership marker is still present on disk.",
                        new InvalidOperationException("Fan override ownership marker was not cleared after restore."));
                    SetFanRecoveryState(
                        FanRecoveryState.RecoveryBlocked,
                        "Fan recovery is blocked because the ownership marker could not be verified as cleared.");
                }
                else
                {
                    SetFanRecoveryState(
                        FanRecoveryState.None,
                        "No pending fan recovery.");
                }
            }
            catch (Exception exception)
            {
                _logger.Error("Reloading the fan override ownership marker after restore failed.", exception);
                SetFanRecoveryState(
                    FanRecoveryState.InspectionFailed,
                    "Inspection failed. Could not verify fan ownership marker.");
            }

            if (result.PowerOperation is null)
            {
                RefreshRestoreSnapshotStatus();
                UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
                StatusMessage = !string.IsNullOrWhiteSpace(result.SuccessMessage)
                    ? result.SuccessMessage
                    : "Fan control was restored to Apple Auto. No original processor snapshot required restoration.";
            }
            else
            {
                _logger.Info("Re-reading power state after restore.");
                try
                {
                    var currentPowerState = await _powerManagementService.ReadCurrentStateAsync(cancellationToken);
                    _lastPowerStateReadSucceeded = true;
                    ApplyPowerState(currentPowerState);
                    ApplyDetectedProfileState(currentPowerState, _lastVerificationResult);
                    RefreshRestoreSnapshotStatus();
                    UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
                    StatusMessage = "Original power settings restored successfully. Power state refreshed.";
                    _logger.Info($"Power-state read completed after restore. Active scheme: {currentPowerState.SchemeId}.");
                }
                catch (OperationCanceledException exception)
                {
                    HandlePostSuccessPowerStateRefreshCanceled(
                        "Original processor settings were restored and verified, but refreshing the displayed power state was canceled. Use Refresh to update the display.",
                        "Power-state UI refresh canceled after successful restore",
                        exception);
                }
                catch (Exception exception)
                {
                    _lastPowerStateReadSucceeded = false;
                    ApplyDetectedProfileState(ProcessorProfileState.Unknown);
                    RefreshRestoreSnapshotStatus();
                    UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
                    StatusMessage = "Original power settings were restored and verified, but refreshing the displayed power state failed. Use Refresh to retry.";
                    _logger.Error(
                        "Power-state UI refresh failed after successful restore.",
                        exception);
                }
            }

            await TryRefreshFanStatusUnderGateAsync(result.ModelVerificationResult.Model, cancellationToken);
        }
        finally
        {
            if (fanGateAcquired)
            {
                _fanOperationGate.Release();
            }

            IsBusy = false;
        }
    }

    private async Task TryRefreshFanStatusUnderGateAsync(
        string? model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model) || !HasUsableFanModelIdentity(_lastVerificationResult))
        {
            return;
        }

        try
        {
            var status = await _fanControlService
                .ReadStatusAsync(model, cancellationToken);
            ApplyFanStatus(status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation during UI status refresh should not fail the overall operation.
        }
        catch (Exception exception)
        {
            _logger.Info($"Post-operation fan status refresh skipped or failed: {exception.Message}");
        }
    }

    private void OnProfileApplyCanceled(OperationCanceledException exception)
    {
        RefreshProfileStateAfterUncertainApply();
        StatusMessage = "Profile application canceled.";
        _logger.Info($"Profile application canceled: {exception.Message}");
    }

    private void OnProfileApplyException(Exception exception)
    {
        StatusMessage = "Profile application failed. Check the log for details.";
        _logger.Error("Profile application failed unexpectedly.", exception);
    }

    private void OnRestoreCanceled(OperationCanceledException exception)
    {
        StatusMessage = "Restore canceled.";
        _logger.Info($"Restore canceled: {exception.Message}");
    }

    private void OnRestoreException(Exception exception)
    {
        StatusMessage = "Restore failed. Check the log for details.";
        _logger.Error("Restore failed unexpectedly.", exception);
    }

    private void HandlePostSuccessPowerStateRefreshCanceled(
        string statusMessage,
        string logMessage,
        OperationCanceledException exception)
    {
        ApplyDetectedProfileState(ProcessorProfileState.Unknown);
        UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
        StatusMessage = statusMessage;
        _logger.Info($"{logMessage}: {exception.Message}");
    }

    private string GetProfileDisplayName(string profileId)
    {
        return _profileCatalog.GetProfiles(_lastVerificationResult)
            .FirstOrDefault(profile => string.Equals(
                profile.Id,
                profileId,
                StringComparison.OrdinalIgnoreCase))
            ?.DisplayName ?? profileId;
    }

    private void ApplyHardware(HardwareSnapshot snapshot)
    {
        MacModel = snapshot.ComputerSystem.Model;
        Cpu = snapshot.Processor?.Name ?? "Unknown";
        CoreThreadCount = snapshot.Processor is null
            ? "Unknown"
            : $"{snapshot.Processor.NumberOfCores} cores / {snapshot.Processor.NumberOfLogicalProcessors} logical processors";
        Gpu = snapshot.VideoControllers.Count == 0
            ? "Unknown"
            : string.Join(Environment.NewLine, snapshot.VideoControllers.Select(FormatVideoController));
        WindowsVersion = snapshot.OperatingSystem is null
            ? "Unknown"
            : $"{snapshot.OperatingSystem.Caption} {snapshot.OperatingSystem.Version} (Build {snapshot.OperatingSystem.BuildNumber}, {snapshot.OperatingSystem.OSArchitecture})";
    }

    private void ApplyCompatibility(ModelVerificationResult verificationResult)
    {
        PlatformSupport = PlatformSupportFormatter.FormatPlatformSupport(verificationResult.PlatformSupport);
        ModelValidation = PlatformSupportFormatter.FormatModelValidation(verificationResult.ValidationLevel);
        CompatibilityDetails = verificationResult.Message;
    }

    private static bool HasUsableFanModelIdentity(
        ModelVerificationResult verificationResult)
    {
        return verificationResult.IsSupportedIntelMac
            && !string.IsNullOrWhiteSpace(verificationResult.Model)
            && !string.Equals(
                verificationResult.Model,
                "Unknown",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasVerifiedFanActivationIdentity(
        ModelVerificationResult verificationResult)
    {
        return verificationResult.IsSupportedIntelMac
            && !string.IsNullOrWhiteSpace(verificationResult.Model)
            && FanIdentitySafetyPolicy
                .EvaluateIdentity(verificationResult.Model)
                .Failures.Count == 0;
    }

    private void LoadApplicationOptions()
    {
        try
        {
            var options = _applicationOptionsService.Load();
            _closeBehavior = options.CloseBehavior;
            _startWithWindows = options.StartWithWindows;
            _startMinimizedToTray = options.StartMinimizedToTray;
        }
        catch (Exception exception)
        {
            _closeBehavior = ApplicationOptionsSnapshot.Default.CloseBehavior;
            _startWithWindows = ApplicationOptionsSnapshot.Default.StartWithWindows;
            _startMinimizedToTray = ApplicationOptionsSnapshot.Default.StartMinimizedToTray;
            _logger.Error(
                "Application options could not be loaded. Safe defaults will be used.",
                exception);
        }
    }

    private void UpdateCloseBehavior(ApplicationCloseBehavior closeBehavior)
    {
        if (closeBehavior == _closeBehavior)
        {
            return;
        }

        try
        {
            _applicationOptionsService.SetCloseBehavior(closeBehavior);
            _closeBehavior = closeBehavior;
            OnPropertyChanged(nameof(MinimizeToTrayOnClose));
            OnPropertyChanged(nameof(ExitApplicationOnClose));
            StatusMessage = closeBehavior == ApplicationCloseBehavior.MinimizeToTray
                ? "Closing the window will keep the application in the system tray."
                : "Closing the window will exit the application completely.";
            _logger.Info($"Window close behavior changed to '{closeBehavior}'.");
        }
        catch (Exception exception)
        {
            OnPropertyChanged(nameof(MinimizeToTrayOnClose));
            OnPropertyChanged(nameof(ExitApplicationOnClose));
            StatusMessage = "Window close behavior could not be changed. Check the log for details.";
            _logger.Error("Updating the window close behavior failed.", exception);
        }
    }

    private void SetLastVerificationResult(ModelVerificationResult verificationResult)
    {
        _lastVerificationResult = verificationResult;
        NotifyFanMonitoringActivationStateChanged();
    }

    private void ApplyPowerState(PowerStateSnapshot snapshot)
    {
        ActivePowerScheme = snapshot.SchemeId.ToString();
        ProcessorMaximumAc = $"{snapshot.ProcessorMaximumAc}%";
        ProcessorMaximumDc = $"{snapshot.ProcessorMaximumDc}%";
        BoostModeAc = PowerBoostModeFormatter.Format(snapshot.BoostModeAc);
        BoostModeDc = PowerBoostModeFormatter.Format(snapshot.BoostModeDc);
    }

    private void ApplyDetectedProfileState(
        PowerStateSnapshot snapshot,
        ModelVerificationResult verificationResult)
    {
        ApplyDetectedProfileState(_processorProfileStateEvaluator.Evaluate(snapshot, verificationResult));
    }

    private void ApplyDetectedProfileState(ProcessorProfileState state)
    {
        _processorProfileState = state;
        RefreshGamingOptimisedSessionState();
    }

    private void RefreshGamingOptimisedSessionState()
    {
        _gamingOptimisedSessionState = _gamingOptimisedSessionStateEvaluator.Evaluate(
            _processorProfileState,
            _restoreSnapshotStore.HasOriginalRestoreSnapshot,
            _fanRecoveryState,
            FanStatus);
        OnPropertyChanged(nameof(GamingOptimisedState));

        DetectedProfileState = _gamingOptimisedSessionState switch
        {
            GamingOptimisedSessionState.Full =>
                "Gaming Optimised is fully active: CPU settings and Maximum Safe RPM fans are verified.",
            GamingOptimisedSessionState.PartialCpuOnly when
                GamingOptimisedSessionStateEvaluator.IsVerifiedAppleAuto(FanStatus) =>
                "Gaming CPU settings are active. Fans are currently using Apple Auto.",
            GamingOptimisedSessionState.PartialCpuOnly when
                FanStatus.BackendState == FanBackendState.InstalledStopped =>
                "Gaming CPU settings are active. BCPC fan override is not active. Enable fan monitoring/control to re-enable Maximum Safe RPM.",
            GamingOptimisedSessionState.PartialCpuOnly =>
                "Gaming CPU settings are active. BCPC does not own a verified fan override.",
            GamingOptimisedSessionState.FanRecoveryPendingOrUnsafe =>
                "Gaming state is not fully verified. Fan recovery or ownership inspection is required.",
            GamingOptimisedSessionState.NoActiveSession when
                _processorProfileState == ProcessorProfileState.GamingOptimisedDetected =>
                "Gaming Optimised settings detected.",
            GamingOptimisedSessionState.NoActiveSession or GamingOptimisedSessionState.Other =>
                "Windows / custom processor settings.",
            _ => "Unknown - power state has not been read."
        };
    }

    private void RefreshRestoreSnapshotStatus()
    {
        if (_restoreSnapshotStore.HasOriginalRestoreSnapshot)
        {
            RestoreSnapshotStatus = "Available - original processor settings can be restored.";
        }
        else if (_fanRecoveryState != FanRecoveryState.None)
        {
            RestoreSnapshotStatus = "Available - fan override recovery can be performed.";
        }
        else
        {
            RestoreSnapshotStatus = "Not available.";
        }
    }

    private void RefreshProfileStateAfterUncertainApply()
    {
        ApplyDetectedProfileState(ProcessorProfileState.Unknown);
        UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
    }

    private void ApplyHardwareFailure()
    {
        MacModel = "Detection failed";
        Cpu = "Detection failed";
        CoreThreadCount = "Detection failed";
        Gpu = "Detection failed";
        WindowsVersion = "Detection failed";
    }

    private void ApplyPowerFailure()
    {
        ActivePowerScheme = "Read failed";
        ProcessorMaximumAc = "Read failed";
        ProcessorMaximumDc = "Read failed";
        BoostModeAc = "Read failed";
        BoostModeDc = "Read failed";
        ApplyDetectedProfileState(ProcessorProfileState.Unknown);
        RefreshRestoreSnapshotStatus();
    }

    private void UpdateProfiles(
        ModelVerificationResult verificationResult,
        bool isPowerStateReadable)
    {
        RefreshRestoreSnapshotStatus();
        ProfileButtons.Clear();

        var hasFanRecoveryContext = _fanRecoveryState != FanRecoveryState.None;
        var isPartialGamingState =
            _gamingOptimisedSessionState == GamingOptimisedSessionState.PartialCpuOnly;

        foreach (var profile in _profileCatalog.GetProfiles(verificationResult))
        {
            ProfileButtons.Add(new ProfileButtonViewModel(
                profile,
                CreateProfileCommand(profile),
                isRestoreSnapshotAvailable: _restoreSnapshotStore.HasOriginalRestoreSnapshot,
                isPowerStateReadable: isPowerStateReadable,
                hasFanRecoveryContext: hasFanRecoveryContext,
                isPartialGamingState: isPartialGamingState));
        }
    }

    private async Task EvaluateStartupRecoveryUnderGateAsync(
        ModelVerificationResult verificationResult,
        CancellationToken cancellationToken)
    {
        FanOverrideOwnershipMarker? marker;
        try
        {
            marker = await _ownershipReader
                .LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Loading fan override ownership marker during startup recovery check failed.", exception);
            SetFanRecoveryState(
                FanRecoveryState.InspectionFailed,
                "Inspection failed. Could not verify fan ownership marker.");
            return;
        }

        if (marker is null)
        {
            SetFanRecoveryState(
                FanRecoveryState.None,
                "No pending fan recovery.");
            return;
        }

        _logger.Info($"Previous-session fan override marker detected. Model={marker.Model}; CreatedAtUtc={marker.CreatedAtUtc:O}.");

        if (verificationResult.PlatformSupport != PlatformSupportStatus.SupportedIntelMac
            || !string.Equals(marker.Model, verificationResult.Model, StringComparison.Ordinal))
        {
            _logger.Info(
                $"Startup fan recovery blocked: detected supported model '{verificationResult.Model}' does not match marker model '{marker.Model}'.");
            SetFanRecoveryState(
                FanRecoveryState.RecoveryBlocked,
                "Fan recovery is blocked because current hardware state does not match the ownership marker.");
            return;
        }

        if (_gamingOptimisedRestoreCoordinator is null)
        {
            SetFanRecoveryState(
                FanRecoveryState.RecoveryBlocked,
                "Fan recovery is blocked because the transactional restore coordinator is not available.");
            return;
        }

        _logger.Info("Attempting automatic startup fan recovery to Apple Auto.");

        try
        {
            var result = await _gamingOptimisedRestoreCoordinator
                .RecoverFansOnlyAsync(verificationResult.Model, cancellationToken);

            if (result.IsSuccessful)
            {
                _logger.Info($"Automatic startup fan recovery completed successfully. Action={result.FanRecovery?.Action}. Marker cleared.");
                SetFanRecoveryState(
                    FanRecoveryState.None,
                    "No pending fan recovery.");
            }
            else
            {
                _logger.Info($"Automatic startup fan recovery was not completed: {result.FailureReason}. Action={result.FanRecovery?.Action}.");
                SetFanRecoveryState(
                    FanRecoveryState.RecoveryBlocked,
                    "Fan recovery is blocked because current hardware state does not match the ownership marker.");
            }
        }
        catch (AppleSmcServiceStateException)
        {
            _logger.Info("Startup fan recovery deferred because AppleSMC service is not running.");
            SetFanRecoveryState(
                FanRecoveryState.PreviousSessionRecoveryPending,
                "Previous fan override detected. Recovery to Apple Auto is pending.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Error("Startup fan recovery failed unexpectedly.", exception);
            SetFanRecoveryState(
                FanRecoveryState.RecoveryBlocked,
                "Fan recovery is blocked because current hardware state does not match the ownership marker.");
        }
    }

    private async Task TryRetryPendingFanRecoveryUnderGateAsync(
        string verifiedModel,
        CancellationToken cancellationToken)
    {
        if (_gamingOptimisedRestoreCoordinator is null)
        {
            return;
        }

        _logger.Info("Retrying pending fan recovery after explicit AppleSMC activation.");

        try
        {
            var result = await _gamingOptimisedRestoreCoordinator
                .RecoverFansOnlyAsync(verifiedModel, cancellationToken);

            if (result.IsSuccessful)
            {
                _logger.Info($"Pending fan recovery completed successfully after AppleSMC activation. Action={result.FanRecovery?.Action}. Marker cleared.");
                SetFanRecoveryState(
                    FanRecoveryState.None,
                    "No pending fan recovery.");

                var refreshedStatus = await _fanControlService
                    .ReadStatusAsync(verifiedModel, cancellationToken);
                ApplyFanStatus(refreshedStatus);
                StatusMessage = "Fan monitoring enabled. Previous fan override was restored to Apple Auto.";
            }
            else
            {
                _logger.Info($"Pending fan recovery after AppleSMC activation was not completed: {result.FailureReason}. Action={result.FanRecovery?.Action}.");
                SetFanRecoveryState(
                    FanRecoveryState.RecoveryBlocked,
                    "Fan recovery is blocked because current hardware state does not match the ownership marker.");
                StatusMessage = "Fan monitoring enabled. Fan recovery remains blocked because current hardware state does not match the ownership marker.";
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Error("Retrying pending fan recovery failed unexpectedly.", exception);
            SetFanRecoveryState(
                FanRecoveryState.RecoveryBlocked,
                "Fan recovery is blocked because current hardware state does not match the ownership marker.");
            StatusMessage = "Fan monitoring enabled, but recovering the previous fan override failed unexpectedly. Check the log for details.";
        }

        UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
    }

    private AsyncCommand CreateProfileCommand(PerformanceProfile profile)
    {
        if (string.Equals(profile.Id, "restore", StringComparison.OrdinalIgnoreCase))
        {
            return new AsyncCommand(
                RestoreOriginalSettingsAsync,
                canExecute: () => !IsBusy,
                onCanceled: OnRestoreCanceled,
                onException: OnRestoreException);
        }

        return new AsyncCommand(
            cancellationToken => ApplyProfileAsync(profile.Id, cancellationToken),
            canExecute: () => !IsBusy,
            onCanceled: OnProfileApplyCanceled,
            onException: OnProfileApplyException);
    }

    private void NotifyOperationCommandsCanExecuteChanged()
    {
        if (RefreshCommand is AsyncCommand refreshCommand)
        {
            refreshCommand.NotifyCanExecuteChanged();
        }

        if (ExportDiagnosticReportCommand is AsyncCommand exportDiagnosticReportCommand)
        {
            exportDiagnosticReportCommand.NotifyCanExecuteChanged();
        }

        if (ReportCompatibilityIssueCommand is AsyncCommand reportCompatibilityIssueCommand)
        {
            reportCompatibilityIssueCommand.NotifyCanExecuteChanged();
        }

        if (EnableFanMonitoringCommand is AsyncCommand enableFanMonitoringCommand)
        {
            enableFanMonitoringCommand.NotifyCanExecuteChanged();
        }

        foreach (var profileButton in ProfileButtons)
        {
            if (profileButton.Command is AsyncCommand profileCommand)
            {
                profileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private void NotifyFanMonitoringActivationStateChanged()
    {
        OnPropertyChanged(nameof(IsFanMonitoringActivationAvailable));

        if (EnableFanMonitoringCommand is AsyncCommand enableFanMonitoringCommand)
        {
            enableFanMonitoringCommand.NotifyCanExecuteChanged();
        }
    }

    private static string FormatHelperOutcome(
        AppleSmcBackendActivationOutcome? helperOutcome)
    {
        return helperOutcome?.ToString() ?? "not provided";
    }

    private static string FormatExitCode(int? exitCode)
    {
        return exitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? "not provided";
    }

    private static string FormatVideoController(VideoControllerInfo videoController)
    {
        return string.IsNullOrWhiteSpace(videoController.DriverVersion)
            ? videoController.Name
            : $"{videoController.Name} (driver {videoController.DriverVersion})";
    }
}
