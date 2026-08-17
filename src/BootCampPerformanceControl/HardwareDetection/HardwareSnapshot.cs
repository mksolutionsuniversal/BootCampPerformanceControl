namespace BootCampPerformanceControl.HardwareDetection;

public sealed record HardwareSnapshot(
    ComputerSystemInfo ComputerSystem,
    ProcessorInfo? Processor,
    IReadOnlyList<VideoControllerInfo> VideoControllers,
    OperatingSystemInfo? OperatingSystem,
    DateTimeOffset CapturedAt);
