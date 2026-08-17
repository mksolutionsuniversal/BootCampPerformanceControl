using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Profiles;

public sealed record ProfileExecutionResolution(
    bool IsExecutable,
    ProcessorPowerSettings? Settings,
    string FailureReason)
{
    public static ProfileExecutionResolution Executable(ProcessorPowerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ProfileExecutionResolution(
            IsExecutable: true,
            settings,
            FailureReason: string.Empty);
    }

    public static ProfileExecutionResolution NotExecutable(string failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        return new ProfileExecutionResolution(
            IsExecutable: false,
            Settings: null,
            failureReason);
    }
}
