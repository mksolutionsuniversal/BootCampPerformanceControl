namespace BootCampPerformanceControl.HardwareDetection;

public sealed class ModelSupportRegistry : IModelSupportRegistry
{
    private static readonly ModelSupportDefinition MacBookPro16_1 = new(
        VerifiedHardwareModels.MacBookPro16_1,
        ModelValidationLevel.PerformanceValidated);

    private static readonly ModelSupportDefinition MacBookPro14_3 = new(
        VerifiedHardwareModels.MacBookPro14_3,
        ModelValidationLevel.NotIndividuallyTested);

    public ModelSupportDefinition? Find(string? modelIdentifier)
    {
        if (string.IsNullOrWhiteSpace(modelIdentifier))
        {
            return null;
        }

        if (string.Equals(
                modelIdentifier,
                MacBookPro16_1.ModelIdentifier,
                StringComparison.OrdinalIgnoreCase))
        {
            return MacBookPro16_1;
        }

        if (string.Equals(
                modelIdentifier,
                MacBookPro14_3.ModelIdentifier,
                StringComparison.OrdinalIgnoreCase))
        {
            return MacBookPro14_3;
        }

        return null;
    }

    public ModelValidationLevel GetValidationLevel(string? modelIdentifier)
    {
        return Find(modelIdentifier)?.ValidationLevel ?? ModelValidationLevel.NotIndividuallyTested;
    }
}
