using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.Profiles;

public sealed class ProfileRestoreService
{
    private readonly IHardwareDetectionService _hardwareDetectionService;
    private readonly IPowerManagementService _powerManagementService;
    private readonly GamingOptimisedRestoreCoordinator? _gamingOptimisedRestoreCoordinator;
    private readonly IRestoreSnapshotStore _restoreSnapshotStore;
    private readonly IFanOverrideOwnershipReader _ownershipReader;
    private readonly IApplicationLogger _logger;

    internal ProfileRestoreService(
        IHardwareDetectionService hardwareDetectionService,
        IPowerManagementService powerManagementService,
        GamingOptimisedRestoreCoordinator? gamingOptimisedRestoreCoordinator,
        IRestoreSnapshotStore restoreSnapshotStore,
        IFanOverrideOwnershipReader ownershipReader,
        IApplicationLogger? logger = null)
    {
        _hardwareDetectionService = hardwareDetectionService ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        _powerManagementService = powerManagementService ?? throw new ArgumentNullException(nameof(powerManagementService));
        _gamingOptimisedRestoreCoordinator = gamingOptimisedRestoreCoordinator;
        _restoreSnapshotStore = restoreSnapshotStore ?? throw new ArgumentNullException(nameof(restoreSnapshotStore));
        _ownershipReader = ownershipReader ?? throw new ArgumentNullException(nameof(ownershipReader));
        _logger = logger ?? NullApplicationLogger.Instance;
    }

    public ProfileRestoreService(
        IHardwareDetectionService hardwareDetectionService,
        IPowerManagementService powerManagementService)
        : this(
            hardwareDetectionService,
            powerManagementService,
            gamingOptimisedRestoreCoordinator: null,
            restoreSnapshotStore: NoOpRestoreSnapshotStore.Instance,
            ownershipReader: NoOpFanOverrideOwnershipReader.Instance,
            logger: NullApplicationLogger.Instance)
    {
    }

    private sealed class NoOpRestoreSnapshotStore : IRestoreSnapshotStore
    {
        public static readonly NoOpRestoreSnapshotStore Instance = new();

        public bool HasOriginalRestoreSnapshot => false;

        public Task<bool> TrySaveOriginalRestoreSnapshotAsync(PowerStateSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<PowerStateSnapshot?> GetOriginalRestoreSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult<PowerStateSnapshot?>(null);

        public Task ReplaceOriginalRestoreSnapshotAsync(PowerStateSnapshot snapshot, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ClearOriginalRestoreSnapshotAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NoOpFanOverrideOwnershipReader : IFanOverrideOwnershipReader
    {
        public static readonly NoOpFanOverrideOwnershipReader Instance = new();

        public Task<FanOverrideOwnershipMarker?> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<FanOverrideOwnershipMarker?>(null);
    }

    public async Task<ProfileRestoreResult> RestoreAsync(CancellationToken cancellationToken)
    {
        var hardwareSnapshot = await _hardwareDetectionService
            .DetectAsync(cancellationToken)
            .ConfigureAwait(false);
        var verificationResult = _hardwareDetectionService.VerifyModel(hardwareSnapshot);

        FanOverrideOwnershipMarker? marker;
        try
        {
            marker = await _ownershipReader
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Loading the fan override ownership marker failed during restore.", exception);
            return ProfileRestoreResult.Failed(
                "Could not verify fan override ownership state. Restore cannot safely proceed.",
                verificationResult);
        }

        if (marker is not null)
        {
            _logger.Info($"Fan override ownership marker detected during restore. Model={marker.Model}.");

            if (verificationResult.PlatformSupport != PlatformSupportStatus.SupportedIntelMac
                || !string.Equals(marker.Model, verificationResult.Model, StringComparison.Ordinal))
            {
                _logger.Info(
                    $"Fan recovery blocked: ownership marker model '{marker.Model}' does not match current verified hardware '{verificationResult.Model}'.");
                return ProfileRestoreResult.Failed(
                    "Fan recovery is blocked because current hardware state does not match the ownership marker.",
                    verificationResult);
            }

            if (_gamingOptimisedRestoreCoordinator is null)
            {
                return ProfileRestoreResult.Failed(
                    "Transactional fan restore coordinator is required while BCPC fan ownership exists.",
                    verificationResult);
            }

            var hasPowerSnapshot = _restoreSnapshotStore.HasOriginalRestoreSnapshot;

            if (marker is not null && !hasPowerSnapshot)
            {
                _logger.Info("Executing fan-only recovery because no processor power snapshot exists.");
                try
                {
                    var fanResult = await _gamingOptimisedRestoreCoordinator
                        .RecoverFansOnlyAsync(verificationResult.Model, cancellationToken)
                        .ConfigureAwait(false);

                    if (!fanResult.IsSuccessful)
                    {
                        return ProfileRestoreResult.Failed(
                            fanResult.FailureReason,
                            verificationResult,
                            powerOperation: null,
                            fanResult.FanRecovery);
                    }

                    return ProfileRestoreResult.SuccessfulFanOnly(
                        verificationResult,
                        fanResult.FanRecovery,
                        "Fan control was restored to Apple Auto. No original processor snapshot required restoration.");
                }
                catch (AppleSmcServiceStateException)
                {
                    return ProfileRestoreResult.Failed(
                        "Fan control is not available because the AppleSMC service is not running. Enable fan monitoring before restoring original settings.",
                        verificationResult);
                }
            }

            try
            {
                var gamingResult = await _gamingOptimisedRestoreCoordinator
                    .RestoreAsync(verificationResult.Model, cancellationToken)
                    .ConfigureAwait(false);

                if (!gamingResult.IsSuccessful)
                {
                    return ProfileRestoreResult.Failed(
                        gamingResult.FailureReason,
                        verificationResult,
                        gamingResult.PowerOperation,
                        gamingResult.FanRecovery);
                }

                return ProfileRestoreResult.Successful(
                    verificationResult,
                    gamingResult.PowerOperation,
                    gamingResult.FanRecovery);
            }
            catch (AppleSmcServiceStateException)
            {
                return ProfileRestoreResult.Failed(
                    "Fan control is not available because the AppleSMC service is not running. Enable fan monitoring before restoring original settings.",
                    verificationResult);
            }
        }

        // Without BCPC fan ownership, Restore remains a processor-only operation.
        var powerOperation = await _powerManagementService
            .RestoreOriginalSettingsAsync(cancellationToken)
            .ConfigureAwait(false);

        return ProfileRestoreResult.FromPowerOperation(powerOperation, verificationResult);
    }
}
