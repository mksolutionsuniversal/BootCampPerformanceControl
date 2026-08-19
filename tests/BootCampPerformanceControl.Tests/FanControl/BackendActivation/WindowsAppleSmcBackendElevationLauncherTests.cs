using System.ComponentModel;
using System.Diagnostics;
using BootCampPerformanceControl.FanControl.BackendActivation;

namespace BootCampPerformanceControl.Tests.FanControl.BackendActivation;

public sealed class WindowsAppleSmcBackendElevationLauncherTests
{
    private const string ExecutablePath =
        @"C:\Program Files\BootCamp Performance Control\BootCampPerformanceControl.exe";

    [Fact]
    public async Task LaunchAsync_UsesExactElevatedHelperProcessConfiguration()
    {
        var runner = new FakeElevatedProcessRunner { ExitCode = 0 };
        var launcher = CreateLauncher(runner);

        var result = await launcher.LaunchAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendElevationOutcome.Completed, result.Outcome);
        Assert.Equal(AppleSmcBackendActivationOutcome.Running, result.HelperOutcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Exception);
        Assert.Equal(1, runner.CallCount);

        var startInfo = Assert.IsType<ProcessStartInfo>(runner.StartInfo);
        Assert.Equal(ExecutablePath, startInfo.FileName);
        Assert.Equal("--start-applesmc", startInfo.Arguments);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb);
        Assert.Equal(Path.GetDirectoryName(ExecutablePath), startInfo.WorkingDirectory);
        Assert.False(startInfo.RedirectStandardInput);
        Assert.False(startInfo.RedirectStandardOutput);
        Assert.False(startInfo.RedirectStandardError);
    }

    [Fact]
    public async Task LaunchAsync_UacCancellation_ReturnsExpectedResult()
    {
        var runner = new FakeElevatedProcessRunner
        {
            Exception = new Win32Exception(1223)
        };
        var launcher = CreateLauncher(runner);

        var result = await launcher.LaunchAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendElevationOutcome.UserCanceled, result.Outcome);
        Assert.Null(result.HelperOutcome);
        Assert.Null(result.ExitCode);
        Assert.Null(result.Exception);
    }

    [Theory]
    [InlineData(10, AppleSmcBackendActivationOutcome.UnsupportedModel)]
    [InlineData(11, AppleSmcBackendActivationOutcome.BackendNotInstalled)]
    [InlineData(12, AppleSmcBackendActivationOutcome.Transitional)]
    [InlineData(13, AppleSmcBackendActivationOutcome.AccessDenied)]
    [InlineData(14, AppleSmcBackendActivationOutcome.Timeout)]
    [InlineData(15, AppleSmcBackendActivationOutcome.Failed)]
    public async Task LaunchAsync_KnownNonZeroExitCode_ReturnsCompletedHelperOutcome(
        int exitCode,
        AppleSmcBackendActivationOutcome expectedOutcome)
    {
        var runner = new FakeElevatedProcessRunner { ExitCode = exitCode };
        var launcher = CreateLauncher(runner);

        var result = await launcher.LaunchAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendElevationOutcome.Completed, result.Outcome);
        Assert.Equal(expectedOutcome, result.HelperOutcome);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task LaunchAsync_UnknownExitCode_ReturnsFailedWithContext()
    {
        var runner = new FakeElevatedProcessRunner { ExitCode = 99 };
        var launcher = CreateLauncher(runner);

        var result = await launcher.LaunchAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendElevationOutcome.Failed, result.Outcome);
        Assert.Null(result.HelperOutcome);
        Assert.Equal(99, result.ExitCode);
        Assert.IsType<InvalidOperationException>(result.Exception);
    }

    [Fact]
    public async Task LaunchAsync_UnexpectedLaunchFailure_RetainsException()
    {
        var expectedException = new Win32Exception(5, "Access denied.");
        var runner = new FakeElevatedProcessRunner
        {
            Exception = expectedException
        };
        var launcher = CreateLauncher(runner);

        var result = await launcher.LaunchAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendElevationOutcome.Failed, result.Outcome);
        Assert.Null(result.HelperOutcome);
        Assert.Null(result.ExitCode);
        Assert.Same(expectedException, result.Exception);
    }

    [Fact]
    public async Task LaunchAsync_CancellationPropagatesToProcessRunner()
    {
        var runner = new FakeElevatedProcessRunner();
        using var cancellationSource = new CancellationTokenSource();
        runner.Callback = (_, cancellationToken) =>
        {
            Assert.Equal(cancellationSource.Token, cancellationToken);
            cancellationSource.Cancel();
            return Task.FromCanceled<int>(cancellationToken);
        };
        var launcher = CreateLauncher(runner);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => launcher.LaunchAsync(cancellationSource.Token));

        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task LaunchAsync_MissingExecutablePath_FailsBeforeProcessStart()
    {
        var runner = new FakeElevatedProcessRunner();
        var launcher = new WindowsAppleSmcBackendElevationLauncher(
            runner,
            static () => null);

        var result = await launcher.LaunchAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendElevationOutcome.Failed, result.Outcome);
        Assert.IsType<InvalidOperationException>(result.Exception);
        Assert.Equal(0, runner.CallCount);
    }

    private static WindowsAppleSmcBackendElevationLauncher CreateLauncher(
        FakeElevatedProcessRunner runner)
    {
        return new WindowsAppleSmcBackendElevationLauncher(
            runner,
            static () => ExecutablePath);
    }

    private sealed class FakeElevatedProcessRunner : IElevatedProcessRunner
    {
        public int CallCount { get; private set; }

        public ProcessStartInfo? StartInfo { get; private set; }

        public int ExitCode { get; init; }

        public Exception? Exception { get; init; }

        public Func<ProcessStartInfo, CancellationToken, Task<int>>? Callback { get; set; }

        public Task<int> RunAsync(
            ProcessStartInfo startInfo,
            CancellationToken cancellationToken)
        {
            CallCount++;
            StartInfo = startInfo;

            if (Callback is not null)
            {
                return Callback(startInfo, cancellationToken);
            }

            if (Exception is not null)
            {
                return Task.FromException<int>(Exception);
            }

            return Task.FromResult(ExitCode);
        }
    }
}
