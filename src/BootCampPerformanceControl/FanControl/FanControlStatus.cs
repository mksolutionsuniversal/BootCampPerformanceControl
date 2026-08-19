using System.Globalization;

namespace BootCampPerformanceControl.FanControl;

public enum FanBackendState
{
    NotChecked,
    NotApplicable,
    NotInstalled,
    InstalledStopped,
    Running,
    Busy,
    AccessDenied,
    Transitional,
    Unavailable,
    Error
}

public enum FanSafetyState
{
    NotChecked,
    UnsupportedModel,
    MonitoringUnavailable,
    ReadOnlyUnavailable,
    ReadOnlyVerified,
    Error
}

public enum FanOperatingMode
{
    AppleAuto,
    Manual,
    Unknown
}

public sealed record FanReading(
    float ActualRpm,
    float MaximumRpm,
    FanOperatingMode Mode);

public sealed record FanControlStatus(
    FanBackendState BackendState,
    FanSafetyState SafetyState,
    FanReading? Fan0,
    FanReading? Fan1,
    string Details)
{
    public bool IsAvailable => SafetyState == FanSafetyState.ReadOnlyVerified;

    public bool IsWriteControlEnabled => false;

    public string BackendDisplayText => BackendState switch
    {
        FanBackendState.NotChecked => "Not checked",
        FanBackendState.NotApplicable => "Not applicable",
        FanBackendState.NotInstalled => "Not installed",
        FanBackendState.InstalledStopped => "Installed, stopped",
        FanBackendState.Running => "Running",
        FanBackendState.Busy => "Busy / in use by another application",
        FanBackendState.AccessDenied => "Access denied",
        FanBackendState.Transitional => "Transition in progress",
        FanBackendState.Unavailable => "Unavailable",
        FanBackendState.Error => "Error",
        _ => "Unknown"
    };

    public string SafetyDisplayText => SafetyState switch
    {
        FanSafetyState.NotChecked => "Not checked",
        FanSafetyState.UnsupportedModel => "Unsupported model",
        FanSafetyState.MonitoringUnavailable => "Monitoring unavailable",
        FanSafetyState.ReadOnlyUnavailable => "Read-only unavailable",
        FanSafetyState.ReadOnlyVerified => "Read-only verified",
        FanSafetyState.Error => "Error",
        _ => "Unknown"
    };

    public string Fan0DisplayText => FormatReading(Fan0);

    public string Fan1DisplayText => FormatReading(Fan1);

    public string ModeDisplayText => FormatModes(Fan0, Fan1);

    public string WriteControlDisplayText => "Disabled";

    public string DisplayText => IsAvailable
        ? $"Fan Control: read-only verified. Fan 0: {FormatReadingWithMode(Fan0)}; "
            + $"Fan 1: {FormatReadingWithMode(Fan1)}. Write control is not enabled."
        : $"Fan Control: {SafetyDisplayText.ToLowerInvariant()}. {Details}";

    public static FanControlStatus NotChecked { get; } = new(
        FanBackendState.NotChecked,
        FanSafetyState.NotChecked,
        Fan0: null,
        Fan1: null,
        "Fan monitoring has not started.");

    public static FanControlStatus CreateUnavailable(
        FanBackendState backendState,
        FanSafetyState safetyState,
        string details)
    {
        return new FanControlStatus(
            backendState,
            safetyState,
            Fan0: null,
            Fan1: null,
            details);
    }

    private static string FormatReading(FanReading? reading)
    {
        return reading is null
            ? "\u2014"
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0:0} / {1:0} RPM",
                reading.ActualRpm,
                reading.MaximumRpm);
    }

    private static string FormatReadingWithMode(FanReading? reading)
    {
        return reading is null
            ? "\u2014"
            : $"{FormatReading(reading)} ({FormatMode(reading.Mode)})";
    }

    private static string FormatModes(FanReading? fan0, FanReading? fan1)
    {
        if (fan0 is null || fan1 is null)
        {
            return "\u2014";
        }

        var fan0Mode = FormatMode(fan0.Mode);
        var fan1Mode = FormatMode(fan1.Mode);

        return fan0.Mode == fan1.Mode
            ? fan0Mode
            : $"Fan 0: {fan0Mode}; Fan 1: {fan1Mode}";
    }

    private static string FormatMode(FanOperatingMode mode)
    {
        return mode switch
        {
            FanOperatingMode.AppleAuto => "Apple Auto",
            FanOperatingMode.Manual => "Manual",
            _ => "Unknown"
        };
    }
}
