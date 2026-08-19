using BootCampPerformanceControl.FanControl.BackendActivation;
using BootCampPerformanceControl.Startup;

namespace BootCampPerformanceControl.Tests.Startup;

public sealed class ApplicationStartupArgumentsTests
{
    [Fact]
    public void Parse_NoArguments_SelectsNormalApplication()
    {
        var result = ApplicationStartupArguments.Parse(Array.Empty<string>());

        Assert.Equal(ApplicationStartupMode.Normal, result);
    }

    [Fact]
    public void Parse_ExactHelperArgument_SelectsAppleSmcHelper()
    {
        var result = ApplicationStartupArguments.Parse(
            new[] { "--start-applesmc" });

        Assert.Equal(ApplicationStartupMode.StartAppleSmcHelper, result);
    }

    [Theory]
    [InlineData("--START-APPLESMC")]
    [InlineData("--start-AppleSmc")]
    [InlineData("--start-applesmc-now")]
    [InlineData(" --start-applesmc")]
    [InlineData("--unknown")]
    public void Parse_NonExactArgument_IsInvalid(string argument)
    {
        var result = ApplicationStartupArguments.Parse(new[] { argument });

        Assert.Equal(ApplicationStartupMode.Invalid, result);
    }

    [Fact]
    public void Parse_AdditionalArgument_IsInvalid()
    {
        var result = ApplicationStartupArguments.Parse(
            new[] { "--start-applesmc", "unexpected" });

        Assert.Equal(ApplicationStartupMode.Invalid, result);
    }

    [Fact]
    public void RequiresMainApplicationInstanceGuard_AppliesOnlyToNormalStartup()
    {
        Assert.True(ApplicationStartupArguments
            .RequiresMainApplicationInstanceGuard(ApplicationStartupMode.Normal));
        Assert.False(ApplicationStartupArguments
            .RequiresMainApplicationInstanceGuard(ApplicationStartupMode.StartAppleSmcHelper));
        Assert.False(ApplicationStartupArguments
            .RequiresMainApplicationInstanceGuard(ApplicationStartupMode.Invalid));
    }

    [Theory]
    [InlineData(AppleSmcBackendActivationOutcome.Running, 0)]
    [InlineData(AppleSmcBackendActivationOutcome.UnsupportedModel, 10)]
    [InlineData(AppleSmcBackendActivationOutcome.BackendNotInstalled, 11)]
    [InlineData(AppleSmcBackendActivationOutcome.Transitional, 12)]
    [InlineData(AppleSmcBackendActivationOutcome.AccessDenied, 13)]
    [InlineData(AppleSmcBackendActivationOutcome.Timeout, 14)]
    [InlineData(AppleSmcBackendActivationOutcome.Failed, 15)]
    public void ActivationOutcome_RoundTripsThroughCentralExitCodeMapping(
        AppleSmcBackendActivationOutcome outcome,
        int expectedExitCode)
    {
        var exitCode = ApplicationExitCodes.FromActivationOutcome(outcome);

        var mapped = ApplicationExitCodes.TryGetActivationOutcome(
            exitCode,
            out var roundTrippedOutcome);

        Assert.Equal(expectedExitCode, exitCode);
        Assert.True(mapped);
        Assert.Equal(outcome, roundTrippedOutcome);
    }

    [Fact]
    public void TryGetActivationOutcome_NonHelperExitCode_IsNotMapped()
    {
        var mapped = ApplicationExitCodes.TryGetActivationOutcome(
            ApplicationExitCodes.InvalidArguments,
            out _);

        Assert.False(mapped);
    }
}
