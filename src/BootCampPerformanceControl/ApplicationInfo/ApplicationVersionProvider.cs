using System.Reflection;

namespace BootCampPerformanceControl.ApplicationInfo;

public static class ApplicationVersionProvider
{
    public const string DefaultUnknownVersion = "Unknown";

    public static string GetInformationalVersion(Assembly? assembly = null)
    {
        assembly ??= typeof(ApplicationVersionProvider).Assembly;

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Trim();
        }

        var version = assembly.GetName().Version?.ToString();
        if (!string.IsNullOrWhiteSpace(version))
        {
            return version.Trim();
        }

        return DefaultUnknownVersion;
    }
}
