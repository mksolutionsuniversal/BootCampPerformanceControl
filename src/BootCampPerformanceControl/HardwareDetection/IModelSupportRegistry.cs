namespace BootCampPerformanceControl.HardwareDetection;

public interface IModelSupportRegistry
{
    ModelSupportDefinition? Find(string? modelIdentifier);

    ModelValidationLevel GetValidationLevel(string? modelIdentifier);
}
