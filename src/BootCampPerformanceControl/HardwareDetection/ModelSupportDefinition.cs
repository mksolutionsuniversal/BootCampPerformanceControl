namespace BootCampPerformanceControl.HardwareDetection;

public sealed record ModelSupportDefinition(
    string ModelIdentifier,
    ModelValidationLevel ValidationLevel,
    string? Notes = null);
