namespace BootCampPerformanceControl.HardwareDetection;

public sealed record ModelSupportDefinition(
    string ModelIdentifier,
    bool ProcessorPowerControlVerified);
