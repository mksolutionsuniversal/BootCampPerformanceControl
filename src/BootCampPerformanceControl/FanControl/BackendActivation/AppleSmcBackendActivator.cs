using System.ComponentModel;
using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.FanControl.BackendActivation;

internal sealed class AppleSmcBackendActivator : IAppleSmcBackendActivator
{
    private const int DefaultMaximumPollAttempts = 100;
    private const int ErrorAccessDenied = 5;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceDoesNotExist = 1060;
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly Func<IAppleSmcStartOnlyServiceController> _openServiceController;
    private readonly int _maximumPollAttempts;
    private readonly TimeSpan _pollInterval;

    public AppleSmcBackendActivator()
        : this(
            static () => new WindowsAppleSmcStartOnlyServiceController(),
            DefaultMaximumPollAttempts,
            DefaultPollInterval)
    {
    }

    internal AppleSmcBackendActivator(
        Func<IAppleSmcStartOnlyServiceController> openServiceController,
        int maximumPollAttempts,
        TimeSpan pollInterval)
    {
        _openServiceController = openServiceController
            ?? throw new ArgumentNullException(nameof(openServiceController));

        if (maximumPollAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPollAttempts),
                "Maximum poll attempts must be greater than zero.");
        }

        if (pollInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "Poll interval cannot be negative.");
        }

        _maximumPollAttempts = maximumPollAttempts;
        _pollInterval = pollInterval;
    }

    public async Task<AppleSmcBackendActivationResult> StartAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var serviceController = _openServiceController()
                ?? throw new InvalidOperationException(
                    "The AppleSMC start-only service controller factory returned no controller.");

            cancellationToken.ThrowIfCancellationRequested();
            var initialState = serviceController.GetState();

            return initialState switch
            {
                AppleSmcServiceState.Running => CreateRunningResult(
                    "The AppleSMC compatibility backend is already running."),
                AppleSmcServiceState.Stopped => await StartStoppedServiceAsync(
                        serviceController,
                        cancellationToken)
                    .ConfigureAwait(false),
                AppleSmcServiceState.StartPending => await WaitForRunningAsync(
                        serviceController,
                        cancellationToken)
                    .ConfigureAwait(false),
                AppleSmcServiceState.StopPending => CreateTransitionalResult(initialState),
                _ => CreateUnexpectedStateResult("activation", initialState)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Win32Exception exception) when (
            exception.NativeErrorCode == ErrorServiceDoesNotExist)
        {
            return new AppleSmcBackendActivationResult(
                AppleSmcBackendActivationOutcome.BackendNotInstalled,
                "The AppleSMC compatibility backend is not installed.",
                exception);
        }
        catch (Win32Exception exception) when (
            exception.NativeErrorCode == ErrorAccessDenied)
        {
            return new AppleSmcBackendActivationResult(
                AppleSmcBackendActivationOutcome.AccessDenied,
                "Access to start the AppleSMC compatibility backend was denied.",
                exception);
        }
        catch (Exception exception)
        {
            return new AppleSmcBackendActivationResult(
                AppleSmcBackendActivationOutcome.Failed,
                $"AppleSMC compatibility backend activation failed: {exception.Message}",
                exception);
        }
    }

    private async Task<AppleSmcBackendActivationResult> StartStoppedServiceAsync(
        IAppleSmcStartOnlyServiceController serviceController,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            serviceController.Start();
        }
        catch (Win32Exception exception) when (
            exception.NativeErrorCode == ErrorServiceAlreadyRunning)
        {
            return await ResolveAlreadyRunningRaceAsync(
                    serviceController,
                    exception,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await WaitForRunningAsync(serviceController, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AppleSmcBackendActivationResult> ResolveAlreadyRunningRaceAsync(
        IAppleSmcStartOnlyServiceController serviceController,
        Win32Exception startException,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observedState = serviceController.GetState();

        return observedState switch
        {
            AppleSmcServiceState.Running => CreateRunningResult(
                "The AppleSMC compatibility backend was started by another process."),
            AppleSmcServiceState.StartPending => await WaitForRunningAsync(
                    serviceController,
                    cancellationToken)
                .ConfigureAwait(false),
            AppleSmcServiceState.StopPending => CreateTransitionalResult(observedState),
            _ => new AppleSmcBackendActivationResult(
                AppleSmcBackendActivationOutcome.Failed,
                $"StartServiceW reported that AppleSMC was already running, but the "
                    + $"verified service state was {FormatState(observedState)}.",
                startException)
        };
    }

    private async Task<AppleSmcBackendActivationResult> WaitForRunningAsync(
        IAppleSmcStartOnlyServiceController serviceController,
        CancellationToken cancellationToken)
    {
        AppleSmcServiceState? lastState = null;

        for (var attempt = 1; attempt <= _maximumPollAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastState = serviceController.GetState();

            switch (lastState.Value)
            {
                case AppleSmcServiceState.Running:
                    return CreateRunningResult(
                        "The AppleSMC compatibility backend is running.");

                case AppleSmcServiceState.StartPending:
                    break;

                case AppleSmcServiceState.StopPending:
                    return CreateTransitionalResult(lastState.Value);

                default:
                    return CreateUnexpectedStateResult(
                        "waiting for Running",
                        lastState.Value);
            }

            if (attempt < _maximumPollAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(_pollInterval, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var exception = new TimeoutException(
            $"AppleSMC did not reach Running after {_maximumPollAttempts} polling attempts. "
                + $"Last observed state: {(lastState.HasValue ? FormatState(lastState.Value) : "none")}.");

        return new AppleSmcBackendActivationResult(
            AppleSmcBackendActivationOutcome.Timeout,
            exception.Message,
            exception);
    }

    private static AppleSmcBackendActivationResult CreateRunningResult(string details)
    {
        return new AppleSmcBackendActivationResult(
            AppleSmcBackendActivationOutcome.Running,
            details);
    }

    private static AppleSmcBackendActivationResult CreateTransitionalResult(
        AppleSmcServiceState state)
    {
        return new AppleSmcBackendActivationResult(
            AppleSmcBackendActivationOutcome.Transitional,
            $"The AppleSMC compatibility backend is in transitional state {FormatState(state)}.");
    }

    private static AppleSmcBackendActivationResult CreateUnexpectedStateResult(
        string operation,
        AppleSmcServiceState state)
    {
        var exception = new InvalidOperationException(
            $"The AppleSMC compatibility backend entered unsupported state "
                + $"{FormatState(state)} during {operation}.");

        return new AppleSmcBackendActivationResult(
            AppleSmcBackendActivationOutcome.Failed,
            exception.Message,
            exception);
    }

    private static string FormatState(AppleSmcServiceState state)
    {
        return $"'{state}' ({(uint)state})";
    }
}
