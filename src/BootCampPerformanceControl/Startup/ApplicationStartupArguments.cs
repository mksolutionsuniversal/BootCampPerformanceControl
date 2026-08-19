namespace BootCampPerformanceControl.Startup;

internal enum ApplicationStartupMode
{
    Normal,
    StartAppleSmcHelper,
    Invalid
}

internal static class ApplicationStartupArguments
{
    internal const string StartAppleSmc = "--start-applesmc";

    internal static ApplicationStartupMode Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            return ApplicationStartupMode.Normal;
        }

        return arguments.Count == 1
            && StringComparer.Ordinal.Equals(arguments[0], StartAppleSmc)
                ? ApplicationStartupMode.StartAppleSmcHelper
                : ApplicationStartupMode.Invalid;
    }

    internal static bool RequiresMainApplicationInstanceGuard(
        ApplicationStartupMode startupMode)
    {
        return startupMode == ApplicationStartupMode.Normal;
    }
}
