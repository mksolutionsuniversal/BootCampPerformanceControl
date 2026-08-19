using System.ComponentModel;
using System.Reflection;
using BootCampPerformanceControl.FanControl.BackendActivation;
using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.Tests.FanControl.BackendActivation;

public sealed class AppleSmcBackendActivatorTests
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceDoesNotExist = 1060;

    [Fact]
    public async Task StartAsync_Running_ReturnsSuccessWithoutStartingService()
    {
        var service = new FakeStartOnlyServiceController(
            AppleSmcServiceState.Running);
        var activator = CreateActivator(service);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.Running, result.Outcome);
        Assert.Equal(1, service.QueryCount);
        Assert.Equal(0, service.StartCount);
        Assert.Equal(1, service.DisposeCount);
    }

    [Fact]
    public async Task StartAsync_Stopped_StartsOnceAndWaitsUntilRunning()
    {
        var service = new FakeStartOnlyServiceController(
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.StartPending,
            AppleSmcServiceState.Running);
        var activator = CreateActivator(service);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.Running, result.Outcome);
        Assert.Equal(3, service.QueryCount);
        Assert.Equal(1, service.StartCount);
        Assert.Equal(1, service.DisposeCount);
    }

    [Fact]
    public async Task StartAsync_StartPending_WaitsWithoutStartingAgain()
    {
        var service = new FakeStartOnlyServiceController(
            AppleSmcServiceState.StartPending,
            AppleSmcServiceState.StartPending,
            AppleSmcServiceState.Running);
        var activator = CreateActivator(service);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.Running, result.Outcome);
        Assert.Equal(0, service.StartCount);
        Assert.Equal(3, service.QueryCount);
    }

    [Fact]
    public async Task StartAsync_StopPending_ReturnsTransitionalWithoutStarting()
    {
        var service = new FakeStartOnlyServiceController(
            AppleSmcServiceState.StopPending);
        var activator = CreateActivator(service);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.Transitional, result.Outcome);
        Assert.Contains("StopPending", result.Details, StringComparison.Ordinal);
        Assert.Equal(0, service.StartCount);
        Assert.Equal(1, service.QueryCount);
    }

    [Theory]
    [InlineData((uint)AppleSmcServiceState.ContinuePending)]
    [InlineData((uint)AppleSmcServiceState.PausePending)]
    [InlineData((uint)AppleSmcServiceState.Paused)]
    [InlineData(99u)]
    public async Task StartAsync_UnexpectedInitialState_FailsClosedWithoutStarting(
        uint rawState)
    {
        var service = new FakeStartOnlyServiceController(
            (AppleSmcServiceState)rawState);
        var activator = CreateActivator(service);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Exception);
        Assert.Equal(0, service.StartCount);
        Assert.Equal(1, service.QueryCount);
    }

    [Fact]
    public async Task StartAsync_MissingService_ReturnsBackendNotInstalled()
    {
        var exception = new Win32Exception(
            ErrorServiceDoesNotExist,
            "Service does not exist.");
        var activator = CreateActivator(() => throw exception);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(
            AppleSmcBackendActivationOutcome.BackendNotInstalled,
            result.Outcome);
        Assert.Same(exception, result.Exception);
    }

    [Fact]
    public async Task StartAsync_AccessDenied_ReturnsExplicitAccessDeniedResult()
    {
        var exception = new Win32Exception(
            ErrorAccessDenied,
            "Access denied.");
        var activator = CreateActivator(() => throw exception);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.AccessDenied, result.Outcome);
        Assert.Same(exception, result.Exception);
    }

    [Fact]
    public async Task StartAsync_AlreadyRunningRace_VerifiesRunningAndSucceeds()
    {
        var exception = new Win32Exception(
            ErrorServiceAlreadyRunning,
            "Already running.");
        var service = new FakeStartOnlyServiceController(
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Running)
        {
            StartException = exception
        };
        var activator = CreateActivator(service);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.Running, result.Outcome);
        Assert.Equal(1, service.StartCount);
        Assert.Equal(2, service.QueryCount);
    }

    [Fact]
    public async Task StartAsync_AlreadyRunningRaceInStartPending_WaitsWithoutDuplicateStart()
    {
        var service = new FakeStartOnlyServiceController(
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.StartPending,
            AppleSmcServiceState.StartPending,
            AppleSmcServiceState.Running)
        {
            StartException = new Win32Exception(
                ErrorServiceAlreadyRunning,
                "Already running.")
        };
        var activator = CreateActivator(service);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.Running, result.Outcome);
        Assert.Equal(1, service.StartCount);
        Assert.Equal(4, service.QueryCount);
    }

    [Fact]
    public async Task StartAsync_AlreadyRunningErrorWithoutSafeState_FailsClosed()
    {
        var exception = new Win32Exception(
            ErrorServiceAlreadyRunning,
            "Already running.");
        var service = new FakeStartOnlyServiceController(
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Stopped)
        {
            StartException = exception
        };
        var activator = CreateActivator(service);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.Failed, result.Outcome);
        Assert.Same(exception, result.Exception);
        Assert.Equal(1, service.StartCount);
        Assert.Equal(2, service.QueryCount);
    }

    [Fact]
    public async Task StartAsync_ServiceReturnsToStoppedAfterAcceptedStart_FailsWithoutRetry()
    {
        var service = new FakeStartOnlyServiceController(
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Stopped);
        var activator = CreateActivator(service);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.Failed, result.Outcome);
        Assert.Equal(1, service.StartCount);
        Assert.Equal(2, service.QueryCount);
    }

    [Fact]
    public async Task StartAsync_StartPendingDoesNotReachRunning_ReturnsBoundedTimeout()
    {
        var service = new FakeStartOnlyServiceController(
            AppleSmcServiceState.StartPending,
            AppleSmcServiceState.StartPending,
            AppleSmcServiceState.StartPending,
            AppleSmcServiceState.StartPending);
        var activator = CreateActivator(service, maximumPollAttempts: 3);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.Timeout, result.Outcome);
        Assert.IsType<TimeoutException>(result.Exception);
        Assert.Contains("3 polling attempts", result.Details, StringComparison.Ordinal);
        Assert.Equal(4, service.QueryCount);
        Assert.Equal(0, service.StartCount);
        Assert.Equal(1, service.DisposeCount);
    }

    [Fact]
    public async Task StartAsync_CancellationDuringPolling_PropagatesAndDisposesController()
    {
        using var cancellationSource = new CancellationTokenSource();
        var service = new FakeStartOnlyServiceController(
            AppleSmcServiceState.StartPending,
            AppleSmcServiceState.StartPending)
        {
            StateQueried = queryCount =>
            {
                if (queryCount == 2)
                {
                    cancellationSource.Cancel();
                }
            }
        };
        var activator = CreateActivator(service);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => activator.StartAsync(cancellationSource.Token));

        Assert.Equal(0, service.StartCount);
        Assert.Equal(1, service.DisposeCount);
    }

    [Fact]
    public async Task StartAsync_UnexpectedStartFailure_ReturnsFailedWithOriginalException()
    {
        var exception = new Win32Exception(1058, "Service disabled.");
        var service = new FakeStartOnlyServiceController(
            AppleSmcServiceState.Stopped)
        {
            StartException = exception
        };
        var activator = CreateActivator(service);

        var result = await activator.StartAsync(CancellationToken.None);

        Assert.Equal(AppleSmcBackendActivationOutcome.Failed, result.Outcome);
        Assert.Same(exception, result.Exception);
        Assert.Equal(1, service.StartCount);
        Assert.Equal(1, service.DisposeCount);
    }

    [Fact]
    public void StartOnlyControllerContract_ContainsNoStopCapability()
    {
        Assert.DoesNotContain(
            typeof(IAppleSmcStartOnlyServiceController).GetMethods(),
            method => string.Equals(method.Name, "Stop", StringComparison.Ordinal));
        Assert.Null(typeof(WindowsAppleSmcStartOnlyServiceController).GetMethod(
            "Stop",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Null(typeof(WindowsAppleSmcStartOnlyServiceController).GetMethod(
            "ControlService",
            BindingFlags.Static | BindingFlags.NonPublic));
    }

    [Fact]
    public void Constructor_InvalidPollSettings_FailFast()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AppleSmcBackendActivator(
                static () => throw new InvalidOperationException(),
                maximumPollAttempts: 0,
                pollInterval: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AppleSmcBackendActivator(
                static () => throw new InvalidOperationException(),
                maximumPollAttempts: 1,
                pollInterval: TimeSpan.FromMilliseconds(-1)));
    }

    private static AppleSmcBackendActivator CreateActivator(
        FakeStartOnlyServiceController service,
        int maximumPollAttempts = 5)
    {
        return CreateActivator(() => service, maximumPollAttempts);
    }

    private static AppleSmcBackendActivator CreateActivator(
        Func<IAppleSmcStartOnlyServiceController> openServiceController,
        int maximumPollAttempts = 5)
    {
        return new AppleSmcBackendActivator(
            openServiceController,
            maximumPollAttempts,
            TimeSpan.Zero);
    }

    private sealed class FakeStartOnlyServiceController
        : IAppleSmcStartOnlyServiceController
    {
        private readonly Queue<AppleSmcServiceState> _states;

        public FakeStartOnlyServiceController(params AppleSmcServiceState[] states)
        {
            _states = new Queue<AppleSmcServiceState>(states);
        }

        public Exception? StartException { get; init; }

        public Action<int>? StateQueried { get; init; }

        public int QueryCount { get; private set; }

        public int StartCount { get; private set; }

        public int DisposeCount { get; private set; }

        public AppleSmcServiceState GetState()
        {
            QueryCount++;

            if (_states.Count == 0)
            {
                throw new InvalidOperationException("No fake service state remains.");
            }

            var state = _states.Dequeue();
            StateQueried?.Invoke(QueryCount);
            return state;
        }

        public void Start()
        {
            StartCount++;

            if (StartException is not null)
            {
                throw StartException;
            }
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
