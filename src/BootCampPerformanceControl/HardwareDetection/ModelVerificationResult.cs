namespace BootCampPerformanceControl.HardwareDetection;

public sealed record ModelVerificationResult(
    string Manufacturer,
    string Model,
    bool IsApple,
    bool IsVerified,
    HardwareVerificationStatus Status,
    string Message)
{
    public static ModelVerificationResult Unknown()
    {
        return new ModelVerificationResult(
            "Unknown",
            "Unknown",
            IsApple: false,
            IsVerified: false,
            HardwareVerificationStatus.Unknown,
            "Hardware has not been detected yet.");
    }
}
