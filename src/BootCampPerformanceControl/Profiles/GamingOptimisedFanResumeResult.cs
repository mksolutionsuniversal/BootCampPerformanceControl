using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Profiles;

internal sealed record GamingOptimisedFanResumeResult(
    bool IsSuccessful,
    string FailureReason,
    ModelVerificationResult ModelVerificationResult,
    FanOverrideExecutionResult? FanExecution)
{
    public static GamingOptimisedFanResumeResult Successful(
        ModelVerificationResult modelVerificationResult,
        FanOverrideExecutionResult fanExecution)
    {
        ArgumentNullException.ThrowIfNull(modelVerificationResult);
        ArgumentNullException.ThrowIfNull(fanExecution);

        return new GamingOptimisedFanResumeResult(
            IsSuccessful: true,
            FailureReason: string.Empty,
            modelVerificationResult,
            fanExecution);
    }

    public static GamingOptimisedFanResumeResult Failed(
        string failureReason,
        ModelVerificationResult modelVerificationResult,
        FanOverrideExecutionResult? fanExecution = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        ArgumentNullException.ThrowIfNull(modelVerificationResult);

        return new GamingOptimisedFanResumeResult(
            IsSuccessful: false,
            failureReason,
            modelVerificationResult,
            fanExecution);
    }
}
