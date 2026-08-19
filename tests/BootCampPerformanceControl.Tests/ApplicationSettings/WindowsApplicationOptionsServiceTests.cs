using BootCampPerformanceControl.ApplicationSettings;

namespace BootCampPerformanceControl.Tests.ApplicationSettings;

public sealed class WindowsApplicationOptionsServiceTests
{
    private const string ExecutablePath =
        @"C:\Program Files\BootCamp Performance Control\BootCampPerformanceControl.exe";
    private const string StaleExecutablePath =
        @"C:\Old Location\BootCampPerformanceControl.exe";

    [Fact]
    public void Load_MissingValues_ReturnsSafeDefaults()
    {
        var registry = new FakeCurrentUserRegistry();
        var service = CreateService(registry);

        var options = service.Load();

        Assert.Equal(ApplicationCloseBehavior.MinimizeToTray, options.CloseBehavior);
        Assert.False(options.StartWithWindows);
    }

    [Fact]
    public void Load_ValidPersistedValues_ReturnsConfiguredOptions()
    {
        var registry = new FakeCurrentUserRegistry();
        registry.SetString(
            WindowsApplicationOptionsService.PreferencesKeyPath,
            WindowsApplicationOptionsService.CloseBehaviorValueName,
            nameof(ApplicationCloseBehavior.ExitApplication));
        registry.SetString(
            WindowsApplicationOptionsService.RunKeyPath,
            WindowsApplicationOptionsService.StartupValueName,
            $"\"{ExecutablePath}\"");
        var service = CreateService(registry);

        var options = service.Load();

        Assert.Equal(ApplicationCloseBehavior.ExitApplication, options.CloseBehavior);
        Assert.True(options.StartWithWindows);
    }

    [Fact]
    public void Load_NoRunValue_ReturnsStartWithWindowsFalse()
    {
        var registry = new FakeCurrentUserRegistry();
        var service = CreateService(registry);

        var options = service.Load();

        Assert.False(options.StartWithWindows);
    }

    [Fact]
    public void Load_StaleStartupCommandUnderApplicationValue_ReturnsStartWithWindowsTrue()
    {
        var registry = new FakeCurrentUserRegistry();
        registry.SetString(
            WindowsApplicationOptionsService.PreferencesKeyPath,
            WindowsApplicationOptionsService.CloseBehaviorValueName,
            "UnknownBehavior");
        registry.SetString(
            WindowsApplicationOptionsService.RunKeyPath,
            WindowsApplicationOptionsService.StartupValueName,
            $"\"{StaleExecutablePath}\"");
        var service = CreateService(registry);

        var options = service.Load();

        Assert.Equal(ApplicationCloseBehavior.MinimizeToTray, options.CloseBehavior);
        Assert.True(options.StartWithWindows);
    }

    [Fact]
    public void SetCloseBehavior_WritesOnlyTheApplicationPreference()
    {
        var registry = new FakeCurrentUserRegistry();
        var service = CreateService(registry);

        service.SetCloseBehavior(ApplicationCloseBehavior.ExitApplication);

        Assert.Equal(
            nameof(ApplicationCloseBehavior.ExitApplication),
            registry.GetString(
                WindowsApplicationOptionsService.PreferencesKeyPath,
                WindowsApplicationOptionsService.CloseBehaviorValueName));
        Assert.Single(registry.Values);
    }

    [Fact]
    public void SetStartWithWindows_EnabledWritesExactQuotedExecutableWithoutArguments()
    {
        var registry = new FakeCurrentUserRegistry();
        var service = CreateService(registry);

        service.SetStartWithWindows(enabled: true);

        Assert.Equal(
            $"\"{ExecutablePath}\"",
            registry.GetString(
                WindowsApplicationOptionsService.RunKeyPath,
                WindowsApplicationOptionsService.StartupValueName));
        Assert.DoesNotContain("--", Assert.Single(registry.Values).Value, StringComparison.Ordinal);
    }

    [Fact]
    public void SetStartWithWindows_EnabledReplacesStaleStartupCommand()
    {
        var registry = new FakeCurrentUserRegistry();
        registry.SetString(
            WindowsApplicationOptionsService.RunKeyPath,
            WindowsApplicationOptionsService.StartupValueName,
            $"\"{StaleExecutablePath}\"");
        var service = CreateService(registry);

        service.SetStartWithWindows(enabled: true);

        Assert.Equal(
            $"\"{ExecutablePath}\"",
            registry.GetString(
                WindowsApplicationOptionsService.RunKeyPath,
                WindowsApplicationOptionsService.StartupValueName));
    }

    [Fact]
    public void SetStartWithWindows_DisabledDeletesCurrentApplicationRunValue()
    {
        var registry = new FakeCurrentUserRegistry();
        registry.SetString(
            WindowsApplicationOptionsService.RunKeyPath,
            WindowsApplicationOptionsService.StartupValueName,
            $"\"{ExecutablePath}\"");
        var service = CreateService(registry);

        service.SetStartWithWindows(enabled: false);

        Assert.Null(registry.GetString(
            WindowsApplicationOptionsService.RunKeyPath,
            WindowsApplicationOptionsService.StartupValueName));
        Assert.Equal(1, registry.DeleteCallCount);
    }

    [Fact]
    public void SetStartWithWindows_DisabledDeletesStaleApplicationRunValue()
    {
        var registry = new FakeCurrentUserRegistry();
        registry.SetString(
            WindowsApplicationOptionsService.RunKeyPath,
            WindowsApplicationOptionsService.StartupValueName,
            $"\"{StaleExecutablePath}\"");
        registry.SetString(
            WindowsApplicationOptionsService.RunKeyPath,
            "OtherApplication",
            "\"C:\\Other\\OtherApplication.exe\"");
        var service = CreateService(registry);

        service.SetStartWithWindows(enabled: false);

        Assert.Null(registry.GetString(
            WindowsApplicationOptionsService.RunKeyPath,
            WindowsApplicationOptionsService.StartupValueName));
        Assert.Equal(
            "\"C:\\Other\\OtherApplication.exe\"",
            registry.GetString(
                WindowsApplicationOptionsService.RunKeyPath,
                "OtherApplication"));
        Assert.Equal(1, registry.DeleteCallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BootCampPerformanceControl.exe")]
    public void SetStartWithWindows_InvalidExecutablePathDoesNotWrite(string? executablePath)
    {
        var registry = new FakeCurrentUserRegistry();
        var service = new WindowsApplicationOptionsService(
            registry,
            () => executablePath);

        Assert.Throws<InvalidOperationException>(
            () => service.SetStartWithWindows(enabled: true));
        Assert.Empty(registry.Values);
    }

    private static WindowsApplicationOptionsService CreateService(
        FakeCurrentUserRegistry registry)
    {
        return new WindowsApplicationOptionsService(
            registry,
            static () => ExecutablePath);
    }

    private sealed class FakeCurrentUserRegistry : ICurrentUserRegistry
    {
        public Dictionary<(string Path, string Name), string> Values { get; } = [];

        public int DeleteCallCount { get; private set; }

        public string? GetString(string subKeyPath, string valueName)
        {
            return Values.GetValueOrDefault((subKeyPath, valueName));
        }

        public void SetString(string subKeyPath, string valueName, string value)
        {
            Values[(subKeyPath, valueName)] = value;
        }

        public void DeleteValue(string subKeyPath, string valueName)
        {
            DeleteCallCount++;
            Values.Remove((subKeyPath, valueName));
        }
    }
}
