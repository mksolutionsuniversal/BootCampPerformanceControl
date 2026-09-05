using System.Reflection;
using BootCampPerformanceControl.ApplicationInfo;

namespace BootCampPerformanceControl.Tests.ApplicationInfo;

public sealed class ApplicationVersionProviderTests
{
    [Fact]
    public void GetInformationalVersion_WithDefaultAssembly_ReturnsCurrentVersion()
    {
        var version = ApplicationVersionProvider.GetInformationalVersion();

        Assert.Equal("0.5.0-rc.1", version);
    }

    [Fact]
    public void GetInformationalVersion_WithTargetAssembly_MatchesInformationalVersionAttribute()
    {
        var targetAssembly = typeof(ApplicationVersionProvider).Assembly;
        var expectedVersion = targetAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var actualVersion = ApplicationVersionProvider.GetInformationalVersion(targetAssembly);

        Assert.NotNull(expectedVersion);
        Assert.Equal(expectedVersion, actualVersion);
    }

    [Fact]
    public void GetInformationalVersion_WithNullAssembly_FallsBackToApplicationAssembly()
    {
        var versionWithNull = ApplicationVersionProvider.GetInformationalVersion(null);
        var expectedVersion = ApplicationVersionProvider.GetInformationalVersion();

        Assert.Equal(expectedVersion, versionWithNull);
    }
}
