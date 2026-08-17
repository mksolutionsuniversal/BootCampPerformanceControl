namespace BootCampPerformanceControl.HardwareDetection;

public sealed record VideoControllerInfo(
    string Name,
    string DriverVersion,
    ulong? AdapterRamBytes);
