using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Profiles;

public interface IProfileCatalog
{
    IReadOnlyList<PerformanceProfile> GetProfiles(ModelVerificationResult verificationResult);
}
