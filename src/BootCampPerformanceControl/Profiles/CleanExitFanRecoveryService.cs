using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.Profiles;

internal sealed class CleanExitFanRecoveryService
{
    private readonly IHardwareDetectionService _hardwareDetectionService;
    private readonly IFanOverrideOwnershipReader _ownershipReader;
    private readonly GamingOptimisedRestoreCoordinator _gamingOptimisedRestoreCoordinator;
    private readonly IApplicationLogger _logger;

    public CleanExitFanRecoveryService(
        IHardwareDetectionService hardwareDetectionService,
        IFanOverrideOwnershipReader ownershipReader,
        GamingOptimisedRestoreCoordinator gamingOptimisedRestoreCoordinator,
        IApplicationLogger logger)
    {
        _hardwareDetectionService = hardwareDetectionService
            ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        _ownershipReader = ownershipReader
            ?? throw new ArgumentNullException(nameof(ownershipReader));
        _gamingOptimisedRestoreCoordinator = gamingOptimisedRestoreCoordinator
            ?? throw new ArgumentNullException(nameof(gamingOptimisedRestoreCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RestoreOwnedFansAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Clean exit fan recovery check started.");

        FanOverrideOwnershipMarker? marker;
        try
        {
            marker = await _ownershipReader
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Error(
                "Clean exit could not read the fan override ownership marker. Fan recovery was not attempted.",
                exception);
            throw;
        }

        if (marker is null)
        {
            _logger.Info("Clean exit fan recovery skipped because no BCPC fan ownership marker exists.");
            return;
        }

        _logger.Info(
            $"Clean exit detected a BCPC fan ownership marker. Model={marker.Model}; CreatedAtUtc={marker.CreatedAtUtc:O}.");

        try
        {
            var hardwareSnapshot = await _hardwareDetectionService
                .DetectAsync(cancellationToken)
                .ConfigureAwait(false);
            var verificationResult = _hardwareDetectionService.VerifyModel(hardwareSnapshot);

            if (verificationResult.PlatformSupport != PlatformSupportStatus.SupportedIntelMac
                || !string.Equals(marker.Model, verificationResult.Model, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Clean exit fan recovery is blocked because ownership marker model '{marker.Model}' "
                    + $"does not match current verified hardware '{verificationResult.Model}'.");
            }

            var result = await _gamingOptimisedRestoreCoordinator
                .RecoverFansOnlyAsync(verificationResult.Model, cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsSuccessful)
            {
                throw new InvalidOperationException(
                    "Clean exit fan recovery did not complete successfully. "
                    + result.FailureReason);
            }

            var markerAfterRecovery = await _ownershipReader
                .LoadAsync(CancellationToken.None)
                .ConfigureAwait(false);

            if (markerAfterRecovery is not null)
            {
                throw new InvalidOperationException(
                    "Clean exit fan recovery returned success but the BCPC fan ownership marker is still present.");
            }

            _logger.Info(
                "Clean exit fan recovery completed successfully. Apple Auto was verified and the fan ownership marker was cleared. Processor power settings were left unchanged.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.Error(
                "Clean exit fan recovery failed. Processor power settings were left unchanged; any surviving fan ownership marker is retained for startup recovery.",
                exception);
            throw;
        }
    }

}
