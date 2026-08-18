namespace BootCampPerformanceControl.HardwareDetection;

public sealed record ModelVerificationResult(
    string Manufacturer,
    string Model,
    PlatformSupportStatus PlatformSupport,
    ModelValidationLevel ValidationLevel,
    string Message)
{
    public bool IsApple => PlatformSupport == PlatformSupportStatus.SupportedIntelMac
        || PlatformSupport == PlatformSupportStatus.UnsupportedNonIntel;

    public bool IsIntelProcessor => PlatformSupport == PlatformSupportStatus.SupportedIntelMac;

    public bool IsSupportedIntelMac => PlatformSupport == PlatformSupportStatus.SupportedIntelMac;

    public bool IsVerified => ValidationLevel == ModelValidationLevel.PerformanceValidated
        || ValidationLevel == ModelValidationLevel.FunctionallyValidated;

    public HardwareVerificationStatus Status => PlatformSupport switch
    {
        PlatformSupportStatus.SupportedIntelMac => ValidationLevel switch
        {
            ModelValidationLevel.PerformanceValidated or ModelValidationLevel.FunctionallyValidated =>
                HardwareVerificationStatus.Verified,
            _ => HardwareVerificationStatus.UnverifiedAppleModel
        },
        PlatformSupportStatus.UnsupportedNonIntel => HardwareVerificationStatus.UnverifiedAppleModel,
        PlatformSupportStatus.UnsupportedNonApple => HardwareVerificationStatus.NonAppleHardware,
        _ => HardwareVerificationStatus.Unknown
    };

    [Obsolete("Legacy constructor for compatibility during Phase 1 transition.")]
    public ModelVerificationResult(
        string Manufacturer,
        string Model,
        bool IsApple,
        bool IsVerified,
        HardwareVerificationStatus Status,
        string Message)
        : this(
            Manufacturer,
            Model,
            !IsApple ? PlatformSupportStatus.UnsupportedNonApple : PlatformSupportStatus.SupportedIntelMac,
            IsVerified ? ModelValidationLevel.PerformanceValidated : ModelValidationLevel.NotIndividuallyTested,
            Message)
    {
    }

    public static ModelVerificationResult Unknown()
    {
        return new ModelVerificationResult(
            "Unknown",
            "Unknown",
            PlatformSupportStatus.DetectionIncomplete,
            ModelValidationLevel.NotIndividuallyTested,
            "Hardware has not been detected yet.");
    }
}
