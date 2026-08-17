using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.UI;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IHardwareDetectionService _hardwareDetectionService;
    private readonly IPowerManagementService _powerManagementService;
    private readonly IProfileCatalog _profileCatalog;
    private readonly ProfileApplyService _profileApplyService;
    private readonly IRestoreSnapshotStore _restoreSnapshotStore;
    private readonly IApplicationLogger _logger;
    private ModelVerificationResult _lastVerificationResult = ModelVerificationResult.Unknown();
    private string _macModel = "Not detected";
    private string _cpu = "Not detected";
    private string _coreThreadCount = "Not detected";
    private string _gpu = "Not detected";
    private string _windowsVersion = "Not detected";
    private string _verifiedModel = "No";
    private string _compatibilityDetails = "Not checked";
    private string _activePowerScheme = "Not read";
    private string _processorMaximumAc = "Not read";
    private string _processorMaximumDc = "Not read";
    private string _boostModeAc = "Not read";
    private string _boostModeDc = "Not read";
    private string _fanControlStatus;
    private string _statusMessage = "Ready";
    private bool _isBusy;

    public MainViewModel(
        IHardwareDetectionService hardwareDetectionService,
        IPowerManagementService powerManagementService,
        IFanControlService fanControlService,
        IProfileCatalog profileCatalog,
        ProfileApplyService profileApplyService,
        IRestoreSnapshotStore restoreSnapshotStore,
        IApplicationLogger logger)
    {
        _hardwareDetectionService = hardwareDetectionService;
        _powerManagementService = powerManagementService;
        _profileCatalog = profileCatalog;
        _profileApplyService = profileApplyService;
        _restoreSnapshotStore = restoreSnapshotStore;
        _logger = logger;
        _fanControlStatus = fanControlService.GetStatus().DisplayText;
        RefreshCommand = new AsyncCommand(
            RefreshAsync,
            canExecute: () => !IsBusy,
            onCanceled: OnRefreshCanceled,
            onException: OnRefreshException);
        UpdateProfiles(_lastVerificationResult);
    }

    public ICommand RefreshCommand { get; }

    public ObservableCollection<ProfileButtonViewModel> ProfileButtons { get; } = [];

    public string ApplicationVersion { get; } = GetApplicationInformationalVersion();

    public string ReadOnlyMessage => "Only verified Gaming Optimised execution is enabled in this milestone.";

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

    public string VerifiedModel
    {
        get => _verifiedModel;
        private set => SetProperty(ref _verifiedModel, value);
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

    public string FanControlStatus
    {
        get => _fanControlStatus;
        private set => SetProperty(ref _fanControlStatus, value);
    }

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

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = "Refreshing...";

        try
        {
            var errors = 0;
            var verificationResult = ModelVerificationResult.Unknown();

            try
            {
                _logger.Info("Hardware detection started.");
                var hardwareSnapshot = await _hardwareDetectionService.DetectAsync(cancellationToken);
                verificationResult = _hardwareDetectionService.VerifyModel(hardwareSnapshot);
                _lastVerificationResult = verificationResult;
                ApplyHardware(hardwareSnapshot);
                ApplyCompatibility(verificationResult);
                UpdateProfiles(verificationResult);
                _logger.Info(
                    $"Hardware detection completed. Detected Mac model: {verificationResult.Model}. Verified: {verificationResult.IsVerified}.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors++;
                ApplyHardwareFailure();
                ApplyCompatibility(verificationResult);
                _lastVerificationResult = verificationResult;
                UpdateProfiles(verificationResult);
                _logger.Error("Hardware detection failed.", exception);
            }

            try
            {
                _logger.Info("Power-state read started.");
                var currentPowerState = await _powerManagementService.ReadCurrentStateAsync(cancellationToken);
                ApplyPowerState(currentPowerState);
                _logger.Info($"Power-state read completed. Active scheme: {currentPowerState.SchemeId}.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors++;
                ApplyPowerFailure();
                _logger.Error("Power-state read failed.", exception);
            }

            StatusMessage = errors == 0
                ? "Refresh completed."
                : "Refresh completed with errors. Check the log for details.";
        }
        finally
        {
            IsBusy = false;
        }
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

    private async Task ApplyProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = $"Applying profile '{profileId}'...";

        try
        {
            _logger.Info($"Profile application started: {profileId}.");
            var result = await _profileApplyService.ApplyProfileAsync(profileId, cancellationToken);

            if (!result.IsSuccessful)
            {
                StatusMessage = $"Profile application failed: {result.FailureReason}";
                _logger.Error(
                    $"Profile application failed for '{profileId}': {result.FailureReason}",
                    new InvalidOperationException(result.FailureReason));
                return;
            }

            _logger.Info($"Profile application succeeded: {profileId}. Re-reading power state.");
            _lastVerificationResult = result.ModelVerificationResult;
            UpdateProfiles(_lastVerificationResult);

            try
            {
                var currentPowerState = await _powerManagementService.ReadCurrentStateAsync(cancellationToken);
                ApplyPowerState(currentPowerState);
                StatusMessage = $"Profile '{profileId}' applied successfully. Power state refreshed.";
                _logger.Info($"Power-state read completed after profile application. Active scheme: {currentPowerState.SchemeId}.");
            }
            catch (Exception exception)
            {
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
                UpdateProfiles(_lastVerificationResult);
                _logger.Error(
                    $"Restore failed: {failureMessage}",
                    new InvalidOperationException(failureMessage));
                return;
            }

            _logger.Info("Restore succeeded. Re-reading power state.");
            UpdateProfiles(_lastVerificationResult);

            try
            {
                var currentPowerState = await _powerManagementService.ReadCurrentStateAsync(cancellationToken);
                ApplyPowerState(currentPowerState);
                StatusMessage = "Original power settings restored successfully. Power state refreshed.";
                _logger.Info($"Power-state read completed after restore. Active scheme: {currentPowerState.SchemeId}.");
            }
            catch (Exception exception)
            {
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
        VerifiedModel = verificationResult.IsVerified ? "Yes" : "No";
        CompatibilityDetails = verificationResult.Message;
    }

    private void ApplyPowerState(PowerStateSnapshot snapshot)
    {
        ActivePowerScheme = snapshot.SchemeId.ToString();
        ProcessorMaximumAc = $"{snapshot.ProcessorMaximumAc}%";
        ProcessorMaximumDc = $"{snapshot.ProcessorMaximumDc}%";
        BoostModeAc = PowerBoostModeFormatter.Format(snapshot.BoostModeAc);
        BoostModeDc = PowerBoostModeFormatter.Format(snapshot.BoostModeDc);
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
    }

    private void UpdateProfiles(ModelVerificationResult verificationResult)
    {
        ProfileButtons.Clear();

        foreach (var profile in _profileCatalog.GetProfiles(verificationResult))
        {
            ProfileButtons.Add(new ProfileButtonViewModel(
                profile,
                CreateProfileCommand(profile),
                _restoreSnapshotStore.HasOriginalRestoreSnapshot));
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

        foreach (var profileButton in ProfileButtons)
        {
            if (profileButton.Command is AsyncCommand profileCommand)
            {
                profileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private static string FormatVideoController(VideoControllerInfo videoController)
    {
        return string.IsNullOrWhiteSpace(videoController.DriverVersion)
            ? videoController.Name
            : $"{videoController.Name} (driver {videoController.DriverVersion})";
    }

    private static string GetApplicationInformationalVersion()
    {
        return typeof(MainViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? typeof(MainViewModel).Assembly.GetName().Version?.ToString()
            ?? "Unknown";
    }
}
