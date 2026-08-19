using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using BootCampPerformanceControl.Startup;

namespace BootCampPerformanceControl.FanControl.BackendActivation;

internal sealed class WindowsAppleSmcBackendElevationLauncher
    : IAppleSmcBackendElevationLauncher
{
    private const int ErrorCancelled = 1223;

    private readonly IElevatedProcessRunner _processRunner;
    private readonly Func<string?> _getCurrentExecutablePath;

    public WindowsAppleSmcBackendElevationLauncher()
        : this(
            new ElevatedProcessRunner(),
            static () => Environment.ProcessPath)
    {
    }

    internal WindowsAppleSmcBackendElevationLauncher(
        IElevatedProcessRunner processRunner,
        Func<string?> getCurrentExecutablePath)
    {
        _processRunner = processRunner
            ?? throw new ArgumentNullException(nameof(processRunner));
        _getCurrentExecutablePath = getCurrentExecutablePath
            ?? throw new ArgumentNullException(nameof(getCurrentExecutablePath));
    }

    public async Task<AppleSmcBackendElevationResult> LaunchAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var executablePath = _getCurrentExecutablePath();

            if (string.IsNullOrWhiteSpace(executablePath)
                || !Path.IsPathFullyQualified(executablePath))
            {
                throw new InvalidOperationException(
                    "The current BootCamp Performance Control executable path could not be determined.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = ApplicationStartupArguments.StartAppleSmc,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty
            };

            var exitCode = await _processRunner
                .RunAsync(startInfo, cancellationToken)
                .ConfigureAwait(false);

            if (!ApplicationExitCodes.TryGetActivationOutcome(
                    exitCode,
                    out var helperOutcome))
            {
                return new AppleSmcBackendElevationResult(
                    AppleSmcBackendElevationOutcome.Failed,
                    HelperOutcome: null,
                    exitCode,
                    new InvalidOperationException(
                        $"The elevated AppleSMC helper returned unknown exit code {exitCode}."));
            }

            return new AppleSmcBackendElevationResult(
                AppleSmcBackendElevationOutcome.Completed,
                helperOutcome,
                exitCode,
                Exception: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Win32Exception exception) when (
            exception.NativeErrorCode == ErrorCancelled)
        {
            return new AppleSmcBackendElevationResult(
                AppleSmcBackendElevationOutcome.UserCanceled,
                HelperOutcome: null,
                ExitCode: null,
                Exception: null);
        }
        catch (Exception exception)
        {
            return new AppleSmcBackendElevationResult(
                AppleSmcBackendElevationOutcome.Failed,
                HelperOutcome: null,
                ExitCode: null,
                exception);
        }
    }
}

internal interface IElevatedProcessRunner
{
    Task<int> RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken);
}

internal sealed class ElevatedProcessRunner : IElevatedProcessRunner
{
    public async Task<int> RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "The elevated AppleSMC helper process could not be started.");

        await process
            .WaitForExitAsync(cancellationToken)
            .ConfigureAwait(false);
        return process.ExitCode;
    }
}
