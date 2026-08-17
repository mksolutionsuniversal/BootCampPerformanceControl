namespace BootCampPerformanceControl.HardwareDetection;

public sealed record OperatingSystemInfo(
    string Caption,
    string Version,
    string BuildNumber,
    string OSArchitecture);
