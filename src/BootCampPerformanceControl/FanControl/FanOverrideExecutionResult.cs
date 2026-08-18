namespace BootCampPerformanceControl.FanControl;

internal sealed record FanOverrideExecutionResult(
    bool IsApplied,
    string Message,
    FanOverrideOwnershipMarker? Marker)
{
    public static FanOverrideExecutionResult Blocked(string message)
    {
        return new FanOverrideExecutionResult(
            IsApplied: false,
            message,
            Marker: null);
    }

    public static FanOverrideExecutionResult Applied(
        FanOverrideOwnershipMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);

        return new FanOverrideExecutionResult(
            IsApplied: true,
            "Maximum safe RPM override was applied by the configured writer.",
            marker);
    }
}
