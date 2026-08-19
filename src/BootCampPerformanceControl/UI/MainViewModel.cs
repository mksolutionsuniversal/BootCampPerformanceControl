using System.Collections.ObjectModel;
using System.Windows.Input;
using BootCampPerformanceControl.ApplicationInfo;
using BootCampPerformanceControl.Diagnostics;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.BackendActivation;
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
    private readonly IProfileCatalog _profileCatalog;
    private readonly ProfileApplyService _profileApplyService;
    private readonly IRestoreSnapshotStore _restoreSnapshotStore;
    private readonly ProcessorProfileStateEvaluator _processorProfileStateEvaluator;
    private readonly IDiagnosticReportService _diagnosticReportService;
    private readonly IDiagnosticReportFileSaveService _diagnosticReportFileSaveService;
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
    private string _restoreSnapshotStatus = "Not available.";
    private StructuredFanControlStatus _fanStatus = StructuredFanControlStatus.NotChecked;
    private string _statusMessage = "Ready";
    private bool _isBusy;
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
        IProfileCatalog profileCatalog,
        ProfileApplyService profileApplyService,
        IRestoreSnapshotStore restoreSnapshotStore,
        ProcessorProfileStateEvaluator processorProfileStateEvaluator,
        IDiagnosticReportService diagnosticReportService,
        IDiagnosticReportFileSaveService diagnosticReportFileSaveService,
        IApplicationLogger logger,
        IUserConfirmationService? userConfirmationService = null,
        TimeSpan? fanPollingInterval = null,
        Func<TimeSpan, CancellationToken, Task>? fanPollingDelayAsync = null)
    {
        ArgumentNullException.ThrowIfNull(hardwareDetectionService);
        ArgumentNullException.ThrowIfNull(powerManagementService);
        ArgumentNullException.ThrowIfNull(fanControlService);
        ArgumentNullException.ThrowIfNull(appleSmcBackendElevationLauncher);
        ArgumentNullException.ThrowIfNull(profileCatalog);
        ArgumentNullException.ThrowIfNull(profileApplyService);
        ArgumentNullException.ThrowIfNull(restoreSnapshotStore);
        ArgumentNullException.ThrowIfNull(processorProfileStateEvaluator);
        ArgumentNullException.ThrowIfNull(diagnosticReportService);
        ArgumentNullException.ThrowIfNull(diagnosticReportFileSaveService);
        ArgumentNullException.ThrowIfNull(logger);

        _hardwareDetectionService = hardwareDetectionService;
        _powerManagementService = powerManagementService;
        _fanControlService = fanControlService;
        _appleSmcBackendElevationLauncher = appleSmcBackendElevationLauncher;
        _profileCatalog = profileCatalog;
        _profileApplyService = profileApplyService;
        _restoreSnapshotStore = restoreSnapshotStore;
        _processorProfileStateEvaluator = processorProfileStateEvaluator;
        _diagnosticReportService = diagnosticReportService;
        _diagnosticReportFileSaveService = diagnosticReportFileSaveService;
        _logger = logger;
        _userConfirmationService = userConfirmationService ?? new WpfUserConfirmationService();
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
        UpdateProfiles(_lastVerificationResult, isPowerStateReadable: false);
    }

    public ICommand RefreshCommand { get; }

    public ICommand EnableFanMonitoringCommand { get; }

    public ICommand ExportDiagnosticReportCommand { get; }

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

            StatusMessage = observedStatus.IsAvailable
                ? "Fan monitoring enabled."
                : $"Fan monitoring was not enabled. Parent verification observed backend '{observedStatus.BackendDisplayText}' and safety '{observedStatus.SafetyDisplayText}'. Helper outcome: '{FormatHelperOutcome(launchResult.HelperOutcome)}'.";
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

    private async Task ApplyProfileAsync(string profileId, CancellationToken cancellationToken)
    {
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
        StatusMessage = $"Applying profile '{profileId}'...";

        try
        {
            _logger.Info($"Profile application started: {profileId}.");
            var result = await _profileApplyService.ApplyProfileAsync(profileId, cancellationToken);

            if (!result.IsSuccessful)
            {
                RefreshProfileStateAfterUncertainApply();
                StatusMessage = $"Profile application failed: {result.FailureReason}";
                _logger.Error(
                    $"Profile application failed for '{profileId}': {result.FailureReason}",
                    new InvalidOperationException(result.FailureReason));
                return;
            }

            _logger.Info($"Profile application succeeded: {profileId}. Re-reading power state.");
            SetLastVerificationResult(result.ModelVerificationResult);

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
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreOriginalSettingsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = "Restoring original power settings...";

        try
        {
            _logger.Info("Restore started.");
            var result = await _powerManagementService.RestoreOriginalSettingsAsync(cancellationToken);

            if (!result.IsSuccessful)
            {
                var failureMessage = result.FailureMessage ?? "Restore operation failed.";
                StatusMessage = $"Restore failed: {failureMessage}";
                UpdateProfiles(_lastVerificationResult, _lastPowerStateReadSucceeded);
                _logger.Error(
                    $"Restore failed: {failureMessage}",
                    new InvalidOperationException(failureMessage));
                return;
            }

            _logger.Info("Restore succeeded. Re-reading power state.");

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
        finally
        {
            IsBusy = false;
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
        DetectedProfileState = state switch
        {
            ProcessorProfileState.GamingOptimisedDetected => "Gaming Optimised settings detected.",
            ProcessorProfileState.Other => "Windows / custom processor settings.",
            _ => "Unknown - power state has not been read."
        };
    }

    private void RefreshRestoreSnapshotStatus()
    {
        RestoreSnapshotStatus = _restoreSnapshotStore.HasOriginalRestoreSnapshot
            ? "Available - original processor settings can be restored."
            : "Not available.";
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

        foreach (var profile in _profileCatalog.GetProfiles(verificationResult))
        {
            ProfileButtons.Add(new ProfileButtonViewModel(
                profile,
                CreateProfileCommand(profile),
                _restoreSnapshotStore.HasOriginalRestoreSnapshot,
                isPowerStateReadable));
        }
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
