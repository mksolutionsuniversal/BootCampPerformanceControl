using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

public sealed record ProfileApplyResult(
    string ProfileId,
    bool IsSuccessful,
    string FailureReason,
    ModelVerificationResult ModelVerificationResult,
    ProfileExecutionResolution? ProfileExecutionResolution,
    PowerOperationResult? PowerOperation)
{
    public static ProfileApplyResult Failed(
        string profileId,
        string failureReason,
        ModelVerificationResult modelVerificationResult,
        ProfileExecutionResolution? profileExecutionResolution = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        ArgumentNullException.ThrowIfNull(modelVerificationResult);

        return new ProfileApplyResult(
            profileId,
            IsSuccessful: false,
            failureReason,
            modelVerificationResult,
            profileExecutionResolution,
            PowerOperation: null);
    }

    public static ProfileApplyResult FromPowerOperation(
        string profileId,
        ModelVerificationResult modelVerificationResult,
        ProfileExecutionResolution profileExecutionResolution,
        PowerOperationResult powerOperation)
    {
        ArgumentNullException.ThrowIfNull(modelVerificationResult);
        ArgumentNullException.ThrowIfNull(profileExecutionResolution);
        ArgumentNullException.ThrowIfNull(powerOperation);

        return new ProfileApplyResult(
            profileId,
            powerOperation.IsSuccessful,
            powerOperation.IsSuccessful
                ? string.Empty
                : powerOperation.FailureMessage ?? "Profile power operation failed.",
            modelVerificationResult,
            profileExecutionResolution,
            powerOperation);
    }
}
