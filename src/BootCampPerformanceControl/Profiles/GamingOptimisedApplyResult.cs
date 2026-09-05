using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

internal sealed record GamingOptimisedApplyResult(
    string ProfileId,
    bool IsSuccessful,
    string FailureReason,
    ProfileExecutionResolution? ProcessorResolution,
    FanProfileExecutionResolution? FanResolution,
    FanOverrideExecutionResult? FanExecution,
    PowerOperationResult? PowerOperation,
    FanOverrideRecoveryDecision? FanCompensation)
{
    public static GamingOptimisedApplyResult Successful(
        string profileId,
        ProfileExecutionResolution processorResolution,
        FanProfileExecutionResolution fanResolution,
        FanOverrideExecutionResult fanExecution,
        PowerOperationResult powerOperation)
    {
        ArgumentNullException.ThrowIfNull(processorResolution);
        ArgumentNullException.ThrowIfNull(fanResolution);
        ArgumentNullException.ThrowIfNull(fanExecution);
        ArgumentNullException.ThrowIfNull(powerOperation);

        return new GamingOptimisedApplyResult(
            profileId,
            IsSuccessful: true,
            FailureReason: string.Empty,
            processorResolution,
            fanResolution,
            fanExecution,
            powerOperation,
            FanCompensation: null);
    }

    public static GamingOptimisedApplyResult SuccessfulProcessorOnly(
        string profileId,
        ProfileExecutionResolution processorResolution,
        PowerOperationResult powerOperation,
        FanProfileExecutionResolution? fanResolution = null,
        FanOverrideExecutionResult? fanExecution = null,
        FanOverrideRecoveryDecision? fanCompensation = null)
    {
        ArgumentNullException.ThrowIfNull(processorResolution);
        ArgumentNullException.ThrowIfNull(powerOperation);

        return new GamingOptimisedApplyResult(
            profileId,
            IsSuccessful: true,
            FailureReason: string.Empty,
            processorResolution,
            fanResolution,
            fanExecution,
            powerOperation,
            fanCompensation);
    }

    public static GamingOptimisedApplyResult Failed(
        string profileId,
        string failureReason,
        ProfileExecutionResolution? processorResolution = null,
        FanProfileExecutionResolution? fanResolution = null,
        FanOverrideExecutionResult? fanExecution = null,
        PowerOperationResult? powerOperation = null,
        FanOverrideRecoveryDecision? fanCompensation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        return new GamingOptimisedApplyResult(
            profileId,
            IsSuccessful: false,
            failureReason,
            processorResolution,
            fanResolution,
            fanExecution,
            powerOperation,
            fanCompensation);
    }
}
