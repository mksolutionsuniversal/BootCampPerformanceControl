using System.ComponentModel;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.Tests.FanControl.Smc;

public sealed class CrystalIdeaAppleSmcSessionTests
{
    [Fact]
    public async Task OpenAsync_InitiallyRunning_OpensAndClosesDeviceWithoutStoppingService()
    {
        var events = new List<string>();
        var service = new FakeAppleSmcServiceController(
            events,
            AppleSmcServiceState.Running);
        var transport = new FakeSmcTransport(events);
        var factory = new FakeAppleSmcTransportFactory(events, transport);

        var session = await OpenAsync(service, factory);

        Assert.Equal(1, factory.OpenCount);

        await session.DisposeAsync();

        Assert.Equal(
            [
                "service:query:Running",
                "device:open",
                "device:close",
                "service:dispose"
            ],
            events);
        Assert.Equal(0, service.StopCount);
    }

    [Fact]
    public async Task OpenAsync_InitiallyStopped_VerifiesRunningBeforeOpenAndClosesBeforeStop()
    {
        var events = new List<string>();
        var service = new FakeAppleSmcServiceController(
            events,
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Stopped);
        var transport = new FakeSmcTransport(events);
        var factory = new FakeAppleSmcTransportFactory(events, transport);

        var session = await OpenAsync(service, factory);
        await session.DisposeAsync();

        Assert.Equal(
            [
                "service:query:Stopped",
                "service:start",
                "service:query:Running",
                "device:open",
                "device:close",
                "service:query:Running",
                "service:stop",
                "service:query:Stopped",
                "service:dispose"
            ],
            events);
        Assert.Equal(1, service.StartCount);
        Assert.Equal(1, service.StopCount);
    }

    [Fact]
    public async Task OpenAsync_ExclusiveDeviceSharingViolationAfterStart_RestoresStoppedState()
    {
        var events = new List<string>();
        var service = new FakeAppleSmcServiceController(
            events,
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Stopped);
        var factory = new FakeAppleSmcTransportFactory(
            events,
            WindowsDeviceIoControlClient.CreateOpenException(
                CrystalIdeaAppleSmcTransport.DevicePath,
                errorCode: 32,
                exclusive: true));

        var exception = await Assert.ThrowsAsync<Win32Exception>(
            () => OpenAsync(service, factory));

        Assert.Equal(32, exception.NativeErrorCode);
        Assert.Contains("exclusively", exception.Message, StringComparison.Ordinal);
        Assert.Contains("already in use", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fan-control application", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ERROR_SHARING_VIOLATION", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            [
                "service:query:Stopped",
                "service:start",
                "service:query:Running",
                "device:open",
                "service:query:Running",
                "service:stop",
                "service:query:Stopped",
                "service:dispose"
            ],
            events);
    }

    [Fact]
    public async Task OpenAsync_ServiceStartFailure_DoesNotOpenDevice()
    {
        var events = new List<string>();
        var service = new FakeAppleSmcServiceController(
            events,
            AppleSmcServiceState.Stopped)
        {
            StartException = new InvalidOperationException("Service start failed.")
        };
        var factory = new FakeAppleSmcTransportFactory(
            events,
            new FakeSmcTransport(events));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OpenAsync(service, factory));

        Assert.Equal("Service start failed.", exception.Message);
        Assert.Equal(
            [
                "service:query:Stopped",
                "service:start",
                "service:dispose"
            ],
            events);
        Assert.Equal(0, factory.OpenCount);
        Assert.Equal(0, service.StopCount);
    }

    [Fact]
    public async Task OpenAsync_ServiceNeverReachesRunning_TimesOutWithoutOpeningDevice()
    {
        var events = new List<string>();
        var service = new FakeAppleSmcServiceController(
            events,
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Stopped);
        var factory = new FakeAppleSmcTransportFactory(
            events,
            new FakeSmcTransport(events));

        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => OpenAsync(service, factory));

        Assert.Contains("5 polling attempts", exception.Message, StringComparison.Ordinal);
        Assert.Equal(7, service.QueryCount);
        Assert.Equal(0, factory.OpenCount);
        Assert.Equal(0, service.StopCount);
        Assert.True(service.IsDisposed);
    }

    [Theory]
    [InlineData((uint)AppleSmcServiceState.StartPending)]
    [InlineData((uint)AppleSmcServiceState.StopPending)]
    [InlineData((uint)AppleSmcServiceState.ContinuePending)]
    [InlineData((uint)AppleSmcServiceState.PausePending)]
    [InlineData((uint)AppleSmcServiceState.Paused)]
    public async Task OpenAsync_UnsupportedInitialState_FailsWithoutMutationOrDeviceOpen(
        uint rawInitialState)
    {
        var initialState = (AppleSmcServiceState)rawInitialState;
        var events = new List<string>();
        var service = new FakeAppleSmcServiceController(events, initialState);
        var factory = new FakeAppleSmcTransportFactory(
            events,
            new FakeSmcTransport(events));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OpenAsync(service, factory));

        Assert.Contains("unsupported initial state", exception.Message, StringComparison.Ordinal);
        Assert.Contains(initialState.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, service.StartCount);
        Assert.Equal(0, service.StopCount);
        Assert.Equal(0, factory.OpenCount);
        Assert.True(service.IsDisposed);
    }

    [Fact]
    public async Task OpenAsync_CancellationDuringStartPolling_PropagatesAfterRestoringStoppedState()
    {
        var events = new List<string>();
        using var cancellationSource = new CancellationTokenSource();
        var service = new FakeAppleSmcServiceController(
            events,
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.StartPending,
            AppleSmcServiceState.StartPending,
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Stopped)
        {
            StateQueried = queryCount =>
            {
                if (queryCount == 2)
                {
                    cancellationSource.Cancel();
                }
            }
        };
        var factory = new FakeAppleSmcTransportFactory(
            events,
            new FakeSmcTransport(events));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => OpenAsync(service, factory, cancellationSource.Token));

        Assert.Equal(
            [
                "service:query:Stopped",
                "service:start",
                "service:query:StartPending",
                "service:query:StartPending",
                "service:query:Running",
                "service:stop",
                "service:query:Stopped",
                "service:dispose"
            ],
            events);
        Assert.Equal(0, factory.OpenCount);
        Assert.Equal(1, service.StopCount);
    }

    [Fact]
    public async Task DisposeAsync_RepeatedCall_ClosesAndStopsOnlyOnce()
    {
        var events = new List<string>();
        var service = new FakeAppleSmcServiceController(
            events,
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Stopped);
        var transport = new FakeSmcTransport(events);
        var factory = new FakeAppleSmcTransportFactory(events, transport);
        var session = await OpenAsync(service, factory);

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(1, service.StopCount);
        Assert.Equal(1, service.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_TransportCloseFailure_StillRestoresStoppedState()
    {
        var events = new List<string>();
        var service = new FakeAppleSmcServiceController(
            events,
            AppleSmcServiceState.Stopped,
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Running,
            AppleSmcServiceState.Stopped);
        var transport = new FakeSmcTransport(events)
        {
            DisposeException = new InvalidOperationException("Device close failed.")
        };
        var factory = new FakeAppleSmcTransportFactory(events, transport);
        var session = await OpenAsync(service, factory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await session.DisposeAsync());

        Assert.Equal("Device close failed.", exception.Message);
        Assert.Equal(1, service.StopCount);
        Assert.True(
            events.IndexOf("device:close") < events.IndexOf("service:stop"));
        Assert.True(service.IsDisposed);
    }

    private static Task<CrystalIdeaAppleSmcSession> OpenAsync(
        FakeAppleSmcServiceController service,
        FakeAppleSmcTransportFactory factory,
        CancellationToken cancellationToken = default)
    {
        return CrystalIdeaAppleSmcSession.OpenAsync(
            service,
            factory,
            cancellationToken,
            maximumPollAttempts: 5,
            pollInterval: TimeSpan.Zero);
    }

    private sealed class FakeAppleSmcServiceController : IAppleSmcServiceController
    {
        private readonly List<string> _events;
        private readonly Queue<AppleSmcServiceState> _states;

        public FakeAppleSmcServiceController(
            List<string> events,
            params AppleSmcServiceState[] states)
        {
            _events = events;
            _states = new Queue<AppleSmcServiceState>(states);
        }

        public Exception? StartException { get; init; }

        public Action<int>? StateQueried { get; init; }

        public int QueryCount { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public int DisposeCount { get; private set; }

        public bool IsDisposed => DisposeCount > 0;

        public AppleSmcServiceState GetState()
        {
            QueryCount++;

            if (_states.Count == 0)
            {
                throw new InvalidOperationException("No fake service state remains.");
            }

            var state = _states.Dequeue();
            _events.Add($"service:query:{state}");
            StateQueried?.Invoke(QueryCount);
            return state;
        }

        public void Start()
        {
            StartCount++;
            _events.Add("service:start");

            if (StartException is not null)
            {
                throw StartException;
            }
        }

        public void Stop()
        {
            StopCount++;
            _events.Add("service:stop");
        }

        public void Dispose()
        {
            DisposeCount++;
            _events.Add("service:dispose");
        }
    }

    private sealed class FakeAppleSmcTransportFactory : IAppleSmcTransportFactory
    {
        private readonly List<string> _events;
        private readonly ISmcTransport? _transport;
        private readonly Exception? _openException;

        public FakeAppleSmcTransportFactory(
            List<string> events,
            ISmcTransport transport)
        {
            _events = events;
            _transport = transport;
        }

        public FakeAppleSmcTransportFactory(
            List<string> events,
            Exception openException)
        {
            _events = events;
            _openException = openException;
        }

        public int OpenCount { get; private set; }

        public ISmcTransport Open()
        {
            OpenCount++;
            _events.Add("device:open");

            if (_openException is not null)
            {
                throw _openException;
            }

            return _transport
                ?? throw new InvalidOperationException("No fake transport was configured.");
        }
    }

    private sealed class FakeSmcTransport : ISmcTransport
    {
        private readonly List<string> _events;

        public FakeSmcTransport(List<string> events)
        {
            _events = events;
        }

        public Exception? DisposeException { get; init; }

        public int DisposeCount { get; private set; }

        public Task<SmcTransportProtocol> GetProtocolAsync(
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Unexpected SMC access.");
        }

        public Task<SmcKeyInfo> GetKeyInfoAsync(
            string key,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Unexpected SMC access.");
        }

        public Task<ReadOnlyMemory<byte>> ReadKeyAsync(
            string key,
            byte length,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Unexpected SMC access.");
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            _events.Add("device:close");

            if (DisposeException is not null)
            {
                throw DisposeException;
            }

            return ValueTask.CompletedTask;
        }
    }
}
