namespace BootCampPerformanceControl.Profiles;

public sealed record PerformanceProfile(
    string Id,
    string DisplayName,
    ProfileScope Scope,
    string? TargetModel,
    bool IsAvailableForDetectedModel,
    IReadOnlyList<ProfileSettingMetadata> Settings,
    string Description);
