namespace BootCampPerformanceControl.FanControl.Smc;

internal sealed record SmcKeyInfo(
    string Key,
    byte Length,
    string Type,
    byte Attributes);
