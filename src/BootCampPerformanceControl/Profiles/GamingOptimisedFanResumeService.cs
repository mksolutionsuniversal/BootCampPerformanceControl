using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.Profiles;

internal sealed class GamingOptimisedFanResumeService
{
    private const string GamingOptimisedProfileId = "gaming-optimised";

    private readonly IHardwareDetectionService _hardwareDetectionService;
    private readonly IProfileCatalog _profileCatalog;
    private readonly ProfileExecutionResolver _profileExecutionResolver;
    private readonly FanProfileExecutionResolver _fanProfileExecutionResolver;
    private readonly IPowerManagementService _powerManagementService;
    private readonly IRestoreSnapshotStore _restoreSnapshotStore;
    private readonly IFanExecutionSessionFactory _fanExecutionSessionFactory;

    public GamingOptimisedFanResumeService(
        IHardwareDetectionService hardwareDetectionService,
        IProfileCatalog profileCatalog,
        ProfileExecutionResolver profileExecutionResolver,
        FanProfileExecutionResolver fanProfileExecutionResolver,
        IPowerManagementService powerManagementService,
        IRestoreSnapshotStore restoreSnapshotStore,
        IFanExecutionSessionFactory fanExecutionSessionFactory)
    {
        _hardwareDetectionService = hardwareDetectionService ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _profileExecutionResolver = profileExecutionResolver ?? throw new ArgumentNullException(nameof(profileExecutionResolver));
        _fanProfileExecutionResolver = fanProfileExecutionResolver ?? throw new ArgumentNullException(nameof(fanProfileExecutionResolver));
        _powerManagementService = powerManagementService ?? throw new ArgumentNullException(nameof(powerManagementService));
        _restoreSnapshotStore = restoreSnapshotStore ?? throw new ArgumentNullException(nameof(restoreSnapshotStore));
        _fanExecutionSessionFactory = fanExecutionSessionFactory ?? throw new ArgumentNullException(nameof(fanExecutionSessionFactory));
    }

    public async Task<GamingOptimisedFanResumeResult> ResumeAsync(
        CancellationToken cancellationToken)
    {
        var hardwareSnapshot = await _hardwareDetectionService
            .DetectAsync(cancellationToken)
            .ConfigureAwait(false);
        var verificationResult = _hardwareDetectionService.VerifyModel(hardwareSnapshot);

        if (!IsExactVerifiedMacBookPro16_1(verificationResult))
        {
            return GamingOptimisedFanResumeResult.Failed(
                $"Maximum Safe RPM resume requires the verified {VerifiedHardwareModels.MacBookPro16_1} model.",
                verificationResult);
        }

        var profile = _profileCatalog
            .GetProfiles(verificationResult)
            .SingleOrDefault(candidate => string.Equals(
                candidate.Id,
                GamingOptimisedProfileId,
                StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            return GamingOptimisedFanResumeResult.Failed(
                "Gaming Optimised profile is not available for the current hardware.",
                verificationResult);
        }

        var processorResolution = _profileExecutionResolver.ResolveProcessorSettings(
            profile,
            verificationResult);
        if (!processorResolution.IsExecutable || processorResolution.Settings is null)
        {
            return GamingOptimisedFanResumeResult.Failed(
                processorResolution.FailureReason,
                verificationResult);
        }

        var originalSnapshot = await _restoreSnapshotStore
            .GetOriginalRestoreSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);
        if (originalSnapshot is null)
        {
            return GamingOptimisedFanResumeResult.Failed(
                "Maximum Safe RPM cannot be resumed because no original processor restore snapshot exists.",
                verificationResult);
        }

        var currentPowerState = await _powerManagementService
            .ReadCurrentStateAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!Matches(currentPowerState, processorResolution.Settings))
        {
            return GamingOptimisedFanResumeResult.Failed(
                "Maximum Safe RPM cannot be resumed because the current processor settings no longer match Gaming Optimised.",
                verificationResult);
        }

        IFanExecutionSession fanSession;
        try
        {
            fanSession = await _fanExecutionSessionFactory
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AppleSmcServiceStateException)
        {
            return GamingOptimisedFanResumeResult.Failed(
                "Fan control is unavailable because AppleSMC is not running. Enable fan monitoring/control before re-enabling Maximum Safe RPM.",
                verificationResult);
        }

        GamingOptimisedFanResumeResult result;
        try
        {
            result = await ResumeWithFanSessionAsync(
                    profile,
                    verificationResult,
                    fanSession,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception operationException)
        {
            await DisposeFanSessionAfterFailureAsync(fanSession, operationException)
                .ConfigureAwait(false);
            throw;
        }

        await DisposeFanSessionAfterResultAsync(fanSession, result)
            .ConfigureAwait(false);
        return result;
    }

    private async Task<GamingOptimisedFanResumeResult> ResumeWithFanSessionAsync(
        PerformanceProfile profile,
        ModelVerificationResult verificationResult,
        IFanExecutionSession fanSession,
        CancellationToken cancellationToken)
    {
        var fanCapability = await fanSession.CapabilityProbe
            .ProbeAsync(verificationResult.Model, cancellationToken)
            .ConfigureAwait(false);
        var fanResolution = _fanProfileExecutionResolver.ResolveMaximumSafeRpmPlan(
            profile,
            verificationResult,
            fanCapability);

        if (!fanResolution.IsExecutable || fanResolution.Plan is null)
        {
            return GamingOptimisedFanResumeResult.Failed(
                fanResolution.FailureReason,
                verificationResult);
        }

        var fanExecution = await fanSession.OverrideCoordinator
            .ApplyMaximumSafeRpmAsync(
                verificationResult.Model,
                fanCapability,
                cancellationToken)
            .ConfigureAwait(false);

        return fanExecution.IsApplied
            ? GamingOptimisedFanResumeResult.Successful(verificationResult, fanExecution)
            : GamingOptimisedFanResumeResult.Failed(
                fanExecution.Message,
                verificationResult,
                fanExecution);
    }

    private static bool Matches(
        PowerStateSnapshot currentState,
        ProcessorPowerSettings gamingSettings)
    {
        return currentState.ProcessorMaximumAc == gamingSettings.ProcessorMaximumAc
            && currentState.ProcessorMaximumDc == gamingSettings.ProcessorMaximumDc
            && currentState.BoostModeAc == gamingSettings.BoostModeAc
            && currentState.BoostModeDc == gamingSettings.BoostModeDc;
    }

    private static bool IsExactVerifiedMacBookPro16_1(
        ModelVerificationResult verificationResult)
    {
        return string.Equals(
                verificationResult.Model,
                VerifiedHardwareModels.MacBookPro16_1,
                StringComparison.Ordinal)
            && verificationResult.PlatformSupport == PlatformSupportStatus.SupportedIntelMac
            && verificationResult.ValidationLevel == ModelValidationLevel.PerformanceValidated;
    }

    private static async Task DisposeFanSessionAfterFailureAsync(
        IFanExecutionSession fanSession,
        Exception operationException)
    {
        try
        {
            await fanSession.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupException)
        {
            throw new FanExecutionSessionCleanupException(
                "Fan execution session cleanup failed after Maximum Safe RPM resume failed.",
                operationException,
                cleanupException);
        }
    }

    private static async Task DisposeFanSessionAfterResultAsync(
        IFanExecutionSession fanSession,
        GamingOptimisedFanResumeResult result)
    {
        try
        {
            await fanSession.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupException) when (!result.IsSuccessful)
        {
            throw new FanExecutionSessionCleanupException(
                "Fan execution session cleanup failed after Maximum Safe RPM resume returned a failed result.",
                result.FailureReason,
                recoveryDecision: null,
                cleanupException);
        }
    }
}
