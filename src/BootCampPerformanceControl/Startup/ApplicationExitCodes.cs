using BootCampPerformanceControl.FanControl.BackendActivation;

namespace BootCampPerformanceControl.Startup;

internal static class ApplicationExitCodes
{
    internal const int Success = 0;
    internal const int ApplicationStartupFailed = 1;
    internal const int InvalidArguments = 2;
    internal const int UnsupportedModel = 10;
    internal const int BackendNotInstalled = 11;
    internal const int Transitional = 12;
    internal const int AccessDenied = 13;
    internal const int Timeout = 14;
    internal const int Failed = 15;

    internal static int FromActivationOutcome(AppleSmcBackendActivationOutcome outcome)
    {
        return outcome switch
        {
            AppleSmcBackendActivationOutcome.Running => Success,
            AppleSmcBackendActivationOutcome.UnsupportedModel => UnsupportedModel,
            AppleSmcBackendActivationOutcome.BackendNotInstalled => BackendNotInstalled,
            AppleSmcBackendActivationOutcome.Transitional => Transitional,
            AppleSmcBackendActivationOutcome.AccessDenied => AccessDenied,
            AppleSmcBackendActivationOutcome.Timeout => Timeout,
            AppleSmcBackendActivationOutcome.Failed => Failed,
            _ => Failed
        };
    }

    internal static bool TryGetActivationOutcome(
        int exitCode,
        out AppleSmcBackendActivationOutcome outcome)
    {
        switch (exitCode)
        {
            case Success:
                outcome = AppleSmcBackendActivationOutcome.Running;
                return true;
            case UnsupportedModel:
                outcome = AppleSmcBackendActivationOutcome.UnsupportedModel;
                return true;
            case BackendNotInstalled:
                outcome = AppleSmcBackendActivationOutcome.BackendNotInstalled;
                return true;
            case Transitional:
                outcome = AppleSmcBackendActivationOutcome.Transitional;
                return true;
            case AccessDenied:
                outcome = AppleSmcBackendActivationOutcome.AccessDenied;
                return true;
            case Timeout:
                outcome = AppleSmcBackendActivationOutcome.Timeout;
                return true;
            case Failed:
                outcome = AppleSmcBackendActivationOutcome.Failed;
                return true;
            default:
                outcome = default;
                return false;
        }
    }
}
