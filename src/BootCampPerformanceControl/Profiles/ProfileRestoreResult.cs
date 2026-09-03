using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

public sealed record ProfileRestoreResult(
    bool IsSuccessful,
    string FailureMessage,
    ModelVerificationResult ModelVerificationResult,
    PowerOperationResult? PowerOperation = null,
    string? SuccessMessage = null)
{
    internal FanOverrideRecoveryDecision? FanRecovery { get; init; }

    internal static ProfileRestoreResult Successful(
        ModelVerificationResult modelVerificationResult,
        PowerOperationResult? powerOperation = null,
        FanOverrideRecoveryDecision? fanRecovery = null,
        string? successMessage = null)
    {
        ArgumentNullException.ThrowIfNull(modelVerificationResult);

        return new ProfileRestoreResult(
            IsSuccessful: true,
            FailureMessage: string.Empty,
            modelVerificationResult,
            powerOperation,
            successMessage)
        {
            FanRecovery = fanRecovery
        };
    }

    internal static ProfileRestoreResult SuccessfulFanOnly(
        ModelVerificationResult modelVerificationResult,
        FanOverrideRecoveryDecision? fanRecovery,
        string successMessage)
    {
        ArgumentNullException.ThrowIfNull(modelVerificationResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(successMessage);

        return new ProfileRestoreResult(
            IsSuccessful: true,
            FailureMessage: string.Empty,
            modelVerificationResult,
            PowerOperation: null,
            SuccessMessage: successMessage)
        {
            FanRecovery = fanRecovery
        };
    }

    internal static ProfileRestoreResult Failed(
        string failureMessage,
        ModelVerificationResult modelVerificationResult,
        PowerOperationResult? powerOperation = null,
        FanOverrideRecoveryDecision? fanRecovery = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);
        ArgumentNullException.ThrowIfNull(modelVerificationResult);

        return new ProfileRestoreResult(
            IsSuccessful: false,
            failureMessage,
            modelVerificationResult,
            powerOperation)
        {
            FanRecovery = fanRecovery
        };
    }

    public static ProfileRestoreResult FromPowerOperation(
        PowerOperationResult powerOperation,
        ModelVerificationResult modelVerificationResult)
    {
        ArgumentNullException.ThrowIfNull(powerOperation);
        ArgumentNullException.ThrowIfNull(modelVerificationResult);

        return new ProfileRestoreResult(
            powerOperation.IsSuccessful,
            powerOperation.IsSuccessful
                ? string.Empty
                : powerOperation.FailureMessage ?? "Restore operation failed.",
            modelVerificationResult,
            powerOperation);
    }
}
