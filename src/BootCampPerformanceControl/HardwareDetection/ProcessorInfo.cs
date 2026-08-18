namespace BootCampPerformanceControl.HardwareDetection;

public sealed record ProcessorInfo(
    string Name,
    string Manufacturer,
    uint NumberOfCores,
    uint NumberOfLogicalProcessors,
    uint MaxClockSpeed)
{
    public bool IsIntel =>
        (!string.IsNullOrWhiteSpace(Manufacturer) && Manufacturer.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        || (!string.IsNullOrWhiteSpace(Name) && Name.Contains("Intel", StringComparison.OrdinalIgnoreCase));
}
