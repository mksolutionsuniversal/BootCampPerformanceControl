using System.Runtime.ExceptionServices;
using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.FanControl.Smc.CrystalIdea;

internal sealed class CrystalIdeaAppleSmcSession : ISmcTransport
{
    private const int DefaultMaximumPollAttempts = 100;
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly IAppleSmcServiceController _serviceController;
    private readonly ISmcTransport _transport;
    private readonly bool _stopServiceOnDispose;
    private readonly int _maximumPollAttempts;
    private readonly TimeSpan _pollInterval;
    private readonly object _disposeSync = new();

    private Task? _disposeTask;
    private bool _disposeStarted;

    private enum ServiceWaitGoal
    {
        Running,
        StartResolution,
        Stopped
    }

    private CrystalIdeaAppleSmcSession(
        IAppleSmcServiceController serviceController,
        ISmcTransport transport,
        bool stopServiceOnDispose,
        int maximumPollAttempts,
        TimeSpan pollInterval)
    {
        _serviceController = serviceController;
        _transport = transport;
        _stopServiceOnDispose = stopServiceOnDispose;
        _maximumPollAttempts = maximumPollAttempts;
        _pollInterval = pollInterval;
    }

    internal static Task<CrystalIdeaAppleSmcSession> OpenInstalledDriverAsync(
        CancellationToken cancellationToken)
    {
        return OpenAsync(
            new WindowsAppleSmcServiceController(),
            new CrystalIdeaAppleSmcTransportFactory(),
            cancellationToken);
    }

    internal static Task<CrystalIdeaAppleSmcSession> OpenReadOnlyAsync(
        IAppleSmcServiceController serviceController,
        IAppleSmcTransportFactory transportFactory,
        CancellationToken cancellationToken)
    {
        return OpenAsync(
            serviceController,
            transportFactory,
            cancellationToken,
            allowServiceStart: false);
    }

    internal static async Task<CrystalIdeaAppleSmcSession> OpenAsync(
        IAppleSmcServiceController serviceController,
        IAppleSmcTransportFactory transportFactory,
        CancellationToken cancellationToken,
        int maximumPollAttempts = DefaultMaximumPollAttempts,
        TimeSpan? pollInterval = null,
        bool allowServiceStart = true)
    {
        ArgumentNullException.ThrowIfNull(serviceController);
        ArgumentNullException.ThrowIfNull(transportFactory);

        if (maximumPollAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPollAttempts),
                "Maximum poll attempts must be greater than zero.");
        }

        var resolvedPollInterval = pollInterval ?? DefaultPollInterval;
        if (resolvedPollInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "Poll interval cannot be negative.");
        }

        var startedService = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var initialState = serviceController.GetState();

            switch (initialState)
            {
                case AppleSmcServiceState.Running:
                    break;

                case AppleSmcServiceState.Stopped:
                    if (!allowServiceStart)
                    {
                        throw new AppleSmcServiceStateException(initialState);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    serviceController.Start();
                    startedService = true;

                    await WaitForServiceStateAsync(
                            serviceController,
                            ServiceWaitGoal.Running,
                            maximumPollAttempts,
                            resolvedPollInterval,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;

                default:
                    if (!allowServiceStart)
                    {
                        throw new AppleSmcServiceStateException(initialState);
                    }

                    throw new InvalidOperationException(
                        $"Windows service '{WindowsAppleSmcServiceController.ServiceName}' "
                        + $"is in unsupported initial state {FormatState(initialState)}. "
                        + "Only Running and Stopped are accepted.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var transport = transportFactory.Open()
                ?? throw new InvalidOperationException(
                    "The AppleSMC transport factory returned no transport.");

            return new CrystalIdeaAppleSmcSession(
                serviceController,
                transport,
                startedService,
                maximumPollAttempts,
                resolvedPollInterval);
        }
        catch (Exception operationException)
        {
            var failures = new List<Exception> { operationException };

            if (startedService)
            {
                try
                {
                    await RestoreStoppedStateAsync(
                            serviceController,
                            maximumPollAttempts,
                            resolvedPollInterval)
                        .ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    failures.Add(cleanupException);
                }
            }

            try
            {
                serviceController.Dispose();
            }
            catch (Exception disposeException)
            {
                failures.Add(disposeException);
            }

            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(operationException).Throw();
            }

            throw new AggregateException(
                "AppleSMC session setup failed and cleanup did not complete cleanly.",
                failures);
        }
    }

    public Task<SmcTransportProtocol> GetProtocolAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _transport.GetProtocolAsync(cancellationToken);
    }

    public Task<SmcKeyInfo> GetKeyInfoAsync(
        string key,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _transport.GetKeyInfoAsync(key, cancellationToken);
    }

    public Task<ReadOnlyMemory<byte>> ReadKeyAsync(
        string key,
        byte length,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _transport.ReadKeyAsync(key, length, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposeStarted = true;
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();

        try
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception transportException)
        {
            failures.Add(transportException);
        }

        if (_stopServiceOnDispose)
        {
            try
            {
                await RestoreStoppedStateAsync(
                        _serviceController,
                        _maximumPollAttempts,
                        _pollInterval)
                    .ConfigureAwait(false);
            }
            catch (Exception serviceException)
            {
                failures.Add(serviceException);
            }
        }

        try
        {
            _serviceController.Dispose();
        }
        catch (Exception controllerException)
        {
            failures.Add(controllerException);
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(
                "AppleSMC session cleanup did not complete cleanly.",
                failures);
        }
    }

    private static async Task RestoreStoppedStateAsync(
        IAppleSmcServiceController serviceController,
        int maximumPollAttempts,
        TimeSpan pollInterval)
    {
        var state = serviceController.GetState();

        if (state == AppleSmcServiceState.Stopped)
        {
            return;
        }

        if (state == AppleSmcServiceState.StartPending)
        {
            state = await WaitForServiceStateAsync(
                    serviceController,
                    ServiceWaitGoal.StartResolution,
                    maximumPollAttempts,
                    pollInterval,
                    CancellationToken.None)
                .ConfigureAwait(false);

            if (state == AppleSmcServiceState.Stopped)
            {
                return;
            }
        }

        if (state == AppleSmcServiceState.StopPending)
        {
            await WaitForServiceStateAsync(
                    serviceController,
                    ServiceWaitGoal.Stopped,
                    maximumPollAttempts,
                    pollInterval,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return;
        }

        if (state != AppleSmcServiceState.Running)
        {
            throw CreateUnexpectedTransitionException("restore", state);
        }

        serviceController.Stop();

        await WaitForServiceStateAsync(
                serviceController,
                ServiceWaitGoal.Stopped,
                maximumPollAttempts,
                pollInterval,
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task<AppleSmcServiceState> WaitForServiceStateAsync(
        IAppleSmcServiceController serviceController,
        ServiceWaitGoal waitGoal,
        int maximumPollAttempts,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        AppleSmcServiceState? lastState = null;

        for (var attempt = 1; attempt <= maximumPollAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastState = serviceController.GetState();

            if (HasReachedWaitGoal(waitGoal, lastState.Value))
            {
                return lastState.Value;
            }

            if (!CanContinueWaiting(waitGoal, lastState.Value))
            {
                throw CreateUnexpectedTransitionException(
                    DescribeWaitGoal(waitGoal),
                    lastState.Value);
            }

            await DelayBeforeNextPollAsync(
                    attempt,
                    maximumPollAttempts,
                    pollInterval,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        throw CreatePollTimeoutException(
            DescribeWaitGoal(waitGoal),
            maximumPollAttempts,
            lastState);
    }

    private static bool HasReachedWaitGoal(
        ServiceWaitGoal waitGoal,
        AppleSmcServiceState state)
    {
        return waitGoal switch
        {
            ServiceWaitGoal.Running => state == AppleSmcServiceState.Running,
            ServiceWaitGoal.StartResolution => state is AppleSmcServiceState.Running or
                AppleSmcServiceState.Stopped,
            ServiceWaitGoal.Stopped => state == AppleSmcServiceState.Stopped,
            _ => throw new ArgumentOutOfRangeException(nameof(waitGoal))
        };
    }

    private static bool CanContinueWaiting(
        ServiceWaitGoal waitGoal,
        AppleSmcServiceState state)
    {
        return waitGoal switch
        {
            ServiceWaitGoal.Running => state is AppleSmcServiceState.Stopped or
                AppleSmcServiceState.StartPending,
            ServiceWaitGoal.StartResolution => state == AppleSmcServiceState.StartPending,
            ServiceWaitGoal.Stopped => state is AppleSmcServiceState.Running or
                AppleSmcServiceState.StopPending,
            _ => throw new ArgumentOutOfRangeException(nameof(waitGoal))
        };
    }

    private static string DescribeWaitGoal(ServiceWaitGoal waitGoal)
    {
        return waitGoal switch
        {
            ServiceWaitGoal.Running => "start to Running",
            ServiceWaitGoal.StartResolution => "cleanup after start to Running or Stopped",
            ServiceWaitGoal.Stopped => "stop to Stopped",
            _ => throw new ArgumentOutOfRangeException(nameof(waitGoal))
        };
    }

    private static async Task DelayBeforeNextPollAsync(
        int completedAttempt,
        int maximumPollAttempts,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        if (completedAttempt >= maximumPollAttempts)
        {
            return;
        }

        if (pollInterval == TimeSpan.Zero)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        await Task.Delay(pollInterval, cancellationToken)
            .ConfigureAwait(false);
    }

    private static TimeoutException CreatePollTimeoutException(
        string waitDescription,
        int maximumPollAttempts,
        AppleSmcServiceState? lastState)
    {
        return new TimeoutException(
            $"Windows service '{WindowsAppleSmcServiceController.ServiceName}' "
            + $"did not complete {waitDescription} after {maximumPollAttempts} polling attempts. "
            + $"Last observed state: {(lastState.HasValue ? FormatState(lastState.Value) : "none")}.");
    }

    private static InvalidOperationException CreateUnexpectedTransitionException(
        string operation,
        AppleSmcServiceState state)
    {
        return new InvalidOperationException(
            $"Windows service '{WindowsAppleSmcServiceController.ServiceName}' entered "
            + $"unsupported state {FormatState(state)} during {operation}.");
    }

    private static string FormatState(AppleSmcServiceState state)
    {
        return $"'{state}' ({(uint)state})";
    }

    private void ThrowIfDisposed()
    {
        lock (_disposeSync)
        {
            ObjectDisposedException.ThrowIf(
                _disposeStarted,
                typeof(CrystalIdeaAppleSmcSession));
        }
    }
}
