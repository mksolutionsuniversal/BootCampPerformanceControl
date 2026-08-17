namespace BootCampPerformanceControl.HardwareDetection;

public sealed class ModelSupportRegistry : IModelSupportRegistry
{
    private static readonly ModelSupportDefinition MacBookPro16_1 = new(
        VerifiedHardwareModels.MacBookPro16_1,
        ProcessorPowerControlVerified: true);

    public ModelSupportDefinition? Find(string? modelIdentifier)
    {
        if (string.IsNullOrWhiteSpace(modelIdentifier))
        {
            return null;
        }

        return string.Equals(
            modelIdentifier,
            MacBookPro16_1.ModelIdentifier,
            StringComparison.OrdinalIgnoreCase)
                ? MacBookPro16_1
                : null;
    }
}
