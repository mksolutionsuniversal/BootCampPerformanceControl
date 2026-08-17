using System.Collections.ObjectModel;
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
    private readonly IRestoreSnapshotStore _restoreSnapshotStore;
    private readonly IProfileCatalog _profileCatalog;
    private readonly IApplicationLogger _logger;
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
        IRestoreSnapshotStore restoreSnapshotStore,
        IFanControlService fanControlService,
        IProfileCatalog profileCatalog,
        IApplicationLogger logger)
    {
        _hardwareDetectionService = hardwareDetectionService;
        _powerManagementService = powerManagementService;
        _restoreSnapshotStore = restoreSnapshotStore;
        _profileCatalog = profileCatalog;
        _logger = logger;
        _fanControlStatus = fanControlService.GetStatus().DisplayText;
        RefreshCommand = new AsyncCommand(
            RefreshAsync,
            onCanceled: OnRefreshCanceled,
            onException: OnRefreshException);
        UpdateProfiles(ModelVerificationResult.Unknown());
    }

    public ICommand RefreshCommand { get; }

    public ObservableCollection<ProfileButtonViewModel> ProfileButtons { get; } = [];

    public string ReadOnlyMessage => "Read-only milestone – power changes are disabled.";

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
        private set => SetProperty(ref _isBusy, value);
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
                UpdateProfiles(verificationResult);
                _logger.Error("Hardware detection failed.", exception);
            }

            try
            {
                _logger.Info("Power-state read started.");
                var currentPowerState = await _powerManagementService.ReadCurrentStateAsync(cancellationToken);
                var restoreSnapshotInitialized = await _restoreSnapshotStore.TrySaveOriginalRestoreSnapshotAsync(
                    currentPowerState,
                    cancellationToken);

                ApplyPowerState(currentPowerState);

                if (restoreSnapshotInitialized)
                {
                    _logger.Info("Original restore snapshot initialized from the first successful power read.");
                }
                else
                {
                    _logger.Info("Original restore snapshot already exists; current refresh did not overwrite it.");
                }

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
            ProfileButtons.Add(new ProfileButtonViewModel(profile));
        }
    }

    private static string FormatVideoController(VideoControllerInfo videoController)
    {
        return string.IsNullOrWhiteSpace(videoController.DriverVersion)
            ? videoController.Name
            : $"{videoController.Name} (driver {videoController.DriverVersion})";
    }
}
