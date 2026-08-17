namespace BootCampPerformanceControl.HardwareDetection;

public sealed record ProcessorInfo(
    string Name,
    string Manufacturer,
    uint NumberOfCores,
    uint NumberOfLogicalProcessors,
    uint MaxClockSpeed);
