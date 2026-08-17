namespace BootCampPerformanceControl.HardwareDetection;

public interface IHardwareDetectionService
{
    Task<HardwareSnapshot> DetectAsync(CancellationToken cancellationToken);

    ModelVerificationResult VerifyModel(HardwareSnapshot snapshot);
}
