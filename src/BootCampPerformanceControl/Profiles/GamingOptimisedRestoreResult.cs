using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

internal sealed record GamingOptimisedRestoreResult(
    string Model,
    bool IsSuccessful,
    bool IsFanBaselineVerified,
    string FailureReason,
    FanOverrideRecoveryDecision? FanRecovery,
    PowerOperationResult? PowerOperation)
{
    public static GamingOptimisedRestoreResult Successful(
        string model,
        FanOverrideRecoveryDecision fanRecovery,
        PowerOperationResult powerOperation)
    {
        ArgumentNullException.ThrowIfNull(fanRecovery);
        ArgumentNullException.ThrowIfNull(powerOperation);

        return new GamingOptimisedRestoreResult(
            model,
            IsSuccessful: true,
            IsFanBaselineVerified: true,
            FailureReason: string.Empty,
            fanRecovery,
            powerOperation);
    }

    public static GamingOptimisedRestoreResult Failed(
        string model,
        string failureReason,
        bool isFanBaselineVerified = false,
        FanOverrideRecoveryDecision? fanRecovery = null,
        PowerOperationResult? powerOperation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        return new GamingOptimisedRestoreResult(
            model,
            IsSuccessful: false,
            isFanBaselineVerified,
            failureReason,
            fanRecovery,
            powerOperation);
    }
}
