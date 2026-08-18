namespace BootCampPerformanceControl.Profiles;

public sealed record PerformanceProfile(
    string Id,
    string DisplayName,
    bool IsAvailableForDetectedModel,
    ProcessorPowerProfileTarget PowerTarget,
    IReadOnlyList<ProfileSettingMetadata> Settings,
    string Description);
