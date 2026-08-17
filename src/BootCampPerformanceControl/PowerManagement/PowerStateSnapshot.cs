namespace BootCampPerformanceControl.PowerManagement;

public sealed record PowerStateSnapshot(
    Guid SchemeId,
    uint ProcessorMaximumAc,
    uint ProcessorMaximumDc,
    uint BoostModeAc,
    uint BoostModeDc,
    DateTimeOffset CapturedAt);
