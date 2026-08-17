namespace BootCampPerformanceControl.PowerManagement;

public static class ProcessorPowerSettingsValidator
{
    public static ProcessorPowerSettingsValidationResult Validate(ProcessorPowerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<string>();

        ValidateRange(
            settings.ProcessorMaximumAc,
            maximum: 100,
            nameof(settings.ProcessorMaximumAc),
            errors);
        ValidateRange(
            settings.ProcessorMaximumDc,
            maximum: 100,
            nameof(settings.ProcessorMaximumDc),
            errors);
        ValidateRange(settings.BoostModeAc, maximum: 6, nameof(settings.BoostModeAc), errors);
        ValidateRange(settings.BoostModeDc, maximum: 6, nameof(settings.BoostModeDc), errors);

        return new ProcessorPowerSettingsValidationResult(errors);
    }

    private static void ValidateRange(
        uint value,
        uint maximum,
        string settingName,
        ICollection<string> errors)
    {
        if (value > maximum)
        {
            errors.Add($"{settingName} must be between 0 and {maximum}; received {value}.");
        }
    }
}

public sealed record ProcessorPowerSettingsValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public string ErrorMessage => string.Join(" ", Errors);
}
