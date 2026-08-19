using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.FanControl.BackendActivation;

internal sealed class AppleSmcBackendActivationHelper
{
    private readonly IHardwareDetectionService _hardwareDetectionService;
    private readonly FanSafetyPolicy _fanSafetyPolicy;
    private readonly IAppleSmcBackendActivator _backendActivator;

    internal AppleSmcBackendActivationHelper(
        IHardwareDetectionService hardwareDetectionService,
        FanSafetyPolicy fanSafetyPolicy,
        IAppleSmcBackendActivator backendActivator)
    {
        _hardwareDetectionService = hardwareDetectionService
            ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        _fanSafetyPolicy = fanSafetyPolicy
            ?? throw new ArgumentNullException(nameof(fanSafetyPolicy));
        _backendActivator = backendActivator
            ?? throw new ArgumentNullException(nameof(backendActivator));
    }

    internal async Task<AppleSmcBackendActivationResult> RunAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await _hardwareDetectionService
            .DetectAsync(cancellationToken)
            .ConfigureAwait(false);
        var verification = _hardwareDetectionService.VerifyModel(snapshot);
        var identity = _fanSafetyPolicy.EvaluateIdentity(verification.Model);

        if (!verification.IsSupportedIntelMac || identity.Failures.Count > 0)
        {
            return new AppleSmcBackendActivationResult(
                AppleSmcBackendActivationOutcome.UnsupportedModel,
                "AppleSMC activation is not permitted for the detected hardware identity.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await _backendActivator
            .StartAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
