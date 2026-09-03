using BootCampPerformanceControl.FanControl;

namespace BootCampPerformanceControl.Profiles;

internal sealed record FanProfileExecutionResolution(
    bool IsExecutable,
    FanMaximumSafeRpmPlan? Plan,
    string FailureReason)
{
    public static FanProfileExecutionResolution Executable(FanMaximumSafeRpmPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new FanProfileExecutionResolution(
            IsExecutable: true,
            plan,
            FailureReason: string.Empty);
    }

    public static FanProfileExecutionResolution NotExecutable(string failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        return new FanProfileExecutionResolution(
            IsExecutable: false,
            Plan: null,
            failureReason);
    }
}
