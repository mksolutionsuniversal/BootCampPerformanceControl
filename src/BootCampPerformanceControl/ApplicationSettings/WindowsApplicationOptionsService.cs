using System.IO;
using Microsoft.Win32;

namespace BootCampPerformanceControl.ApplicationSettings;

internal sealed class WindowsApplicationOptionsService : IApplicationOptionsService
{
    internal const string PreferencesKeyPath = @"Software\BootCampPerformanceControl";
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string CloseBehaviorValueName = "CloseBehavior";
    internal const string StartupValueName = "BootCampPerformanceControl";
    internal const string StartMinimizedToTrayValueName = "StartMinimizedToTray";

    private readonly ICurrentUserRegistry _registry;
    private readonly Func<string?> _getExecutablePath;

    public WindowsApplicationOptionsService()
        : this(
            new WindowsCurrentUserRegistry(),
            static () => Environment.ProcessPath)
    {
    }

    internal WindowsApplicationOptionsService(
        ICurrentUserRegistry registry,
        Func<string?> getExecutablePath)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _getExecutablePath = getExecutablePath
            ?? throw new ArgumentNullException(nameof(getExecutablePath));
    }

    public ApplicationOptionsSnapshot Load()
    {
        var closeBehavior = ParseCloseBehavior(
            _registry.GetString(PreferencesKeyPath, CloseBehaviorValueName));
        var configuredStartupCommand = _registry.GetString(
            RunKeyPath,
            StartupValueName);
        var startMinimizedToTray = ParseBooleanPreference(
            _registry.GetString(
                PreferencesKeyPath,
                StartMinimizedToTrayValueName));

        return new ApplicationOptionsSnapshot(
            closeBehavior,
            configuredStartupCommand is not null,
            startMinimizedToTray);
    }

    public void SetCloseBehavior(ApplicationCloseBehavior closeBehavior)
    {
        if (!Enum.IsDefined(closeBehavior))
        {
            throw new ArgumentOutOfRangeException(
                nameof(closeBehavior),
                closeBehavior,
                "Unknown application close behavior.");
        }

        _registry.SetString(
            PreferencesKeyPath,
            CloseBehaviorValueName,
            closeBehavior.ToString());
    }

    public void SetStartWithWindows(bool enabled)
    {
        if (!enabled)
        {
            _registry.DeleteValue(RunKeyPath, StartupValueName);
            return;
        }

        var startupCommand = TryCreateStartupCommand(_getExecutablePath())
            ?? throw new InvalidOperationException(
                "The current BootCamp Performance Control executable path could not be determined.");

        _registry.SetString(
            RunKeyPath,
            StartupValueName,
            startupCommand);
    }

    public void SetStartMinimizedToTray(bool enabled)
    {
        _registry.SetString(
            PreferencesKeyPath,
            StartMinimizedToTrayValueName,
            enabled.ToString());
    }

    private static ApplicationCloseBehavior ParseCloseBehavior(string? storedValue)
    {
        return storedValue switch
        {
            nameof(ApplicationCloseBehavior.ExitApplication) =>
                ApplicationCloseBehavior.ExitApplication,
            nameof(ApplicationCloseBehavior.MinimizeToTray) =>
                ApplicationCloseBehavior.MinimizeToTray,
            _ => ApplicationOptionsSnapshot.Default.CloseBehavior
        };
    }

    private static bool ParseBooleanPreference(string? storedValue)
    {
        return bool.TryParse(storedValue, out var value) && value;
    }

    private static string? TryCreateStartupCommand(string? executablePath)
    {
        return string.IsNullOrWhiteSpace(executablePath)
            || !Path.IsPathFullyQualified(executablePath)
                ? null
                : $"\"{executablePath}\"";
    }
}

internal interface ICurrentUserRegistry
{
    string? GetString(string subKeyPath, string valueName);

    void SetString(string subKeyPath, string valueName, string value);

    void DeleteValue(string subKeyPath, string valueName);
}

internal sealed class WindowsCurrentUserRegistry : ICurrentUserRegistry
{
    public string? GetString(string subKeyPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void SetString(string subKeyPath, string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true)
            ?? throw new InvalidOperationException(
                $"Current-user registry key '{subKeyPath}' could not be opened.");
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteValue(string subKeyPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
