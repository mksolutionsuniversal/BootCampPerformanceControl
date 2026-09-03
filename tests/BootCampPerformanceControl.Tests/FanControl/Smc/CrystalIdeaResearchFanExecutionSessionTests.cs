using System.Buffers.Binary;
using System.Text;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Tests.TestDoubles;

namespace BootCampPerformanceControl.Tests.FanControl.Smc;

public sealed class CrystalIdeaResearchFanExecutionSessionTests
{
    [Fact]
    public async Task OpenAsync_ServiceRunning_OpensSingleSharedDeviceWithoutStartingOrStoppingService()
    {
        var service = new FakeAppleSmcServiceController(AppleSmcServiceState.Running);
        var device = new FakeAppleSmcDevice();
        var openDeviceCount = 0;
        var factory = CreateFactory(
            service,
            () =>
            {
                openDeviceCount++;
                return device;
            });

        var session = await factory.OpenAsync(CancellationToken.None);
        await session.DisposeAsync();

        Assert.Equal(1, service.GetStateCallCount);
        Assert.Equal(0, service.StartCallCount);
        Assert.Equal(0, service.StopCallCount);
        Assert.Equal(1, openDeviceCount);
        Assert.Equal(1, device.DisposeCallCount);
        Assert.Equal(1, service.DisposeCallCount);
    }

    [Theory]
    [InlineData((uint)AppleSmcServiceState.Stopped)]
    [InlineData((uint)AppleSmcServiceState.StartPending)]
    [InlineData((uint)AppleSmcServiceState.StopPending)]
    [InlineData((uint)AppleSmcServiceState.ContinuePending)]
    [InlineData((uint)AppleSmcServiceState.PausePending)]
    [InlineData((uint)AppleSmcServiceState.Paused)]
    public async Task OpenAsync_ServiceNotRunning_RefusesBeforeDeviceOpenWithoutServiceMutation(
        uint rawServiceState)
    {
        var serviceState = (AppleSmcServiceState)rawServiceState;
        var service = new FakeAppleSmcServiceController(serviceState);
        var openDeviceCount = 0;
        var factory = CreateFactory(
            service,
            () =>
            {
                openDeviceCount++;
                return new FakeAppleSmcDevice();
            });

        var exception = await Assert.ThrowsAsync<AppleSmcServiceStateException>(
            () => factory.OpenAsync(CancellationToken.None));

        Assert.Equal(serviceState, exception.State);
        Assert.Equal(1, service.GetStateCallCount);
        Assert.Equal(0, service.StartCallCount);
        Assert.Equal(0, service.StopCallCount);
        Assert.Equal(0, openDeviceCount);
        Assert.Equal(1, service.DisposeCallCount);
    }

    [Fact]
    public async Task OpenAsync_PreCanceledToken_DoesNotQueryServiceOrOpenDevice()
    {
        var serviceFactoryCallCount = 0;
        var openDeviceCount = 0;
        var factory = new CrystalIdeaResearchFanExecutionSessionFactory(
            new TestApplicationLogger(),
            () =>
            {
                serviceFactoryCallCount++;
                return new FakeAppleSmcServiceController(AppleSmcServiceState.Running);
            },
            () =>
            {
                openDeviceCount++;
                return new FakeAppleSmcDevice();
            },
            static () => new InMemoryFanOverrideOwnershipStore());
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => factory.OpenAsync(cancellationSource.Token));

        Assert.Equal(0, serviceFactoryCallCount);
        Assert.Equal(0, openDeviceCount);
    }

    [Fact]
    public async Task OpenAsync_CancellationAfterServiceCheck_DisposesServiceWithoutOpeningDevice()
    {
        using var cancellationSource = new CancellationTokenSource();
        var service = new FakeAppleSmcServiceController(AppleSmcServiceState.Running)
        {
            OnGetState = () => cancellationSource.Cancel()
        };
        var openDeviceCount = 0;
        var factory = CreateFactory(
            service,
            () =>
            {
                openDeviceCount++;
                return new FakeAppleSmcDevice();
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => factory.OpenAsync(cancellationSource.Token));

        Assert.Equal(1, service.GetStateCallCount);
        Assert.Equal(0, openDeviceCount);
        Assert.Equal(1, service.DisposeCallCount);
    }

    [Fact]
    public async Task OpenAsync_CreatesSessionWithoutIssuingIoctl()
    {
        var service = new FakeAppleSmcServiceController(AppleSmcServiceState.Running);
        var device = new FakeAppleSmcDevice();
        var factory = CreateFactory(service, () => device);

        var session = await factory.OpenAsync(CancellationToken.None);

        Assert.Equal(0, device.InvocationCount);

        await session.DisposeAsync();
    }

    [Fact]
    public async Task Session_CapabilityProbeAndFanOverrideWriterUseSameDeviceClient()
    {
        var service = new FakeAppleSmcServiceController(AppleSmcServiceState.Running);
        var device = new FakeAppleSmcDevice();
        var factory = CreateFactory(service, () => device);
        await using var session = await factory.OpenAsync(CancellationToken.None);

        var capability = await session.CapabilityProbe.ProbeAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        var fanExecution = await session.OverrideCoordinator.ApplyMaximumSafeRpmAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            capability,
            CancellationToken.None);

        Assert.True(fanExecution.IsApplied);
        Assert.True(device.ProtocolReadCount > 0);
        Assert.True(device.KeyInfoReadCount > 0);
        Assert.True(device.KeyReadCount > 0);
        Assert.True(device.WriteCount > 0);
        Assert.Equal(1, service.GetStateCallCount);
        Assert.Equal(0, service.StartCallCount);
        Assert.Equal(0, service.StopCallCount);
    }

    [Fact]
    public async Task ReadTransportDispose_WithNonOwningClient_DoesNotDisposeSharedDevice()
    {
        var device = new FakeAppleSmcDevice();
        var transport = new CrystalIdeaAppleSmcTransport(
            new NonOwningDeviceIoControlClient(device));

        await transport.DisposeAsync();

        Assert.Equal(0, device.DisposeCallCount);
    }

    [Fact]
    public async Task WriteBackendDispose_WithNonOwningClient_DoesNotDisposeSharedDevice()
    {
        var device = new FakeAppleSmcDevice();
        var backend = new CrystalIdeaResearchFanSmcWriteBackend(
            new NonOwningDeviceIoControlClient(device));

        await backend.DisposeAsync();

        Assert.Equal(0, device.DisposeCallCount);
    }

    [Fact]
    public async Task DisposeAsync_DisposesSharedDeviceAndServiceOnce()
    {
        var service = new FakeAppleSmcServiceController(AppleSmcServiceState.Running);
        var device = new FakeAppleSmcDevice();
        var factory = CreateFactory(service, () => device);
        var session = await factory.OpenAsync(CancellationToken.None);

        await session.DisposeAsync();

        Assert.Equal(1, device.DisposeCallCount);
        Assert.Equal(1, service.DisposeCallCount);
    }

    [Fact]
    public async Task DisposeAsync_RepeatedCall_DisposesSharedDeviceAndServiceOnlyOnce()
    {
        var service = new FakeAppleSmcServiceController(AppleSmcServiceState.Running);
        var device = new FakeAppleSmcDevice();
        var factory = CreateFactory(service, () => device);
        var session = await factory.OpenAsync(CancellationToken.None);

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, device.DisposeCallCount);
        Assert.Equal(1, service.DisposeCallCount);
    }

    [Fact]
    public async Task OpenAsync_OwnershipStoreFactoryFailure_DisposesDeviceAndService()
    {
        var setupException = new InvalidOperationException("ownership store failed");
        var service = new FakeAppleSmcServiceController(AppleSmcServiceState.Running);
        var device = new FakeAppleSmcDevice();
        var openDeviceCount = 0;
        var factory = CreateFactory(
            service,
            () =>
            {
                openDeviceCount++;
                return device;
            },
            () => throw setupException);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.OpenAsync(CancellationToken.None));

        Assert.Same(setupException, exception);
        Assert.Equal(1, openDeviceCount);
        Assert.Equal(1, device.DisposeCallCount);
        Assert.Equal(1, service.DisposeCallCount);
        Assert.Equal(0, service.StartCallCount);
        Assert.Equal(0, service.StopCallCount);
    }

    [Fact]
    public async Task OpenAsync_DeviceOpenFailure_DisposesServiceWithoutMutation()
    {
        var setupException = new InvalidOperationException("device open failed");
        var service = new FakeAppleSmcServiceController(AppleSmcServiceState.Running);
        var openDeviceCount = 0;
        var factory = CreateFactory(
            service,
            () =>
            {
                openDeviceCount++;
                throw setupException;
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.OpenAsync(CancellationToken.None));

        Assert.Same(setupException, exception);
        Assert.Equal(1, openDeviceCount);
        Assert.Equal(1, service.DisposeCallCount);
        Assert.Equal(0, service.StartCallCount);
        Assert.Equal(0, service.StopCallCount);
    }

    private static CrystalIdeaResearchFanExecutionSessionFactory CreateFactory(
        FakeAppleSmcServiceController serviceController,
        Func<IDeviceIoControlClient> openDeviceClient,
        Func<IFanOverrideOwnershipStore>? openOwnershipStore = null)
    {
        return new CrystalIdeaResearchFanExecutionSessionFactory(
            new TestApplicationLogger(),
            () => serviceController,
            openDeviceClient,
            openOwnershipStore ?? (static () => new InMemoryFanOverrideOwnershipStore()));
    }

    private static byte[] Float32(float value)
    {
        var bytes = new byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes,
            BitConverter.SingleToInt32Bits(value));
        return bytes;
    }

    private static SmcRegister Register(
        string key,
        string type,
        byte attributes,
        ReadOnlySpan<byte> rawData)
    {
        return new SmcRegister(
            key,
            checked((byte)rawData.Length),
            type,
            attributes,
            rawData);
    }

    private sealed class FakeAppleSmcServiceController : IAppleSmcServiceController
    {
        private readonly AppleSmcServiceState _state;

        public FakeAppleSmcServiceController(AppleSmcServiceState state)
        {
            _state = state;
        }

        public int GetStateCallCount { get; private set; }

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public Action? OnGetState { get; init; }

        public AppleSmcServiceState GetState()
        {
            GetStateCallCount++;
            OnGetState?.Invoke();
            return _state;
        }

        public void Start()
        {
            StartCallCount++;
        }

        public void Stop()
        {
            StopCallCount++;
        }

        public void Dispose()
        {
            DisposeCallCount++;
        }
    }

    private sealed class FakeAppleSmcDevice : IDeviceIoControlClient
    {
        private readonly Dictionary<string, SmcRegister> _registers = new(StringComparer.Ordinal)
        {
            ["FNum"] = Register("FNum", "ui8 ", 0x80, [2]),
            ["F0Mx"] = Register("F0Mx", "flt ", 0x85, Float32(5321.25f)),
            ["F1Mx"] = Register("F1Mx", "flt ", 0x85, Float32(4789.5f)),
            ["F0Ac"] = Register("F0Ac", "flt ", 0x84, Float32(1800f)),
            ["F1Ac"] = Register("F1Ac", "flt ", 0x84, Float32(1700f)),
            ["F0Md"] = Register("F0Md", "ui8 ", 0xD0, [0]),
            ["F1Md"] = Register("F1Md", "ui8 ", 0xD0, [0]),
            ["F0Tg"] = Register("F0Tg", "flt ", 0xD4, Float32(1800f)),
            ["F1Tg"] = Register("F1Tg", "flt ", 0xD4, Float32(1700f))
        };

        public int InvocationCount { get; private set; }

        public int ProtocolReadCount { get; private set; }

        public int KeyInfoReadCount { get; private set; }

        public int KeyReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public byte[] Invoke(
            uint controlCode,
            ReadOnlyMemory<byte> input,
            int outputBufferLength)
        {
            InvocationCount++;

            return controlCode switch
            {
                CrystalIdeaAppleSmcIoctl.GetProtocol => GetProtocol(input, outputBufferLength),
                CrystalIdeaAppleSmcIoctl.GetKeyInfo => GetKeyInfo(input, outputBufferLength),
                CrystalIdeaAppleSmcIoctl.ReadKey => ReadKey(input, outputBufferLength),
                CrystalIdeaAppleSmcIoctl.WriteKey => WriteKey(input, outputBufferLength),
                _ => throw new InvalidOperationException(
                    $"Unexpected IOCTL 0x{controlCode:X8}.")
            };
        }

        public void Dispose()
        {
            DisposeCallCount++;
        }

        private byte[] GetProtocol(
            ReadOnlyMemory<byte> input,
            int outputBufferLength)
        {
            Assert.True(input.IsEmpty);
            Assert.Equal(1, outputBufferLength);
            ProtocolReadCount++;
            return [(byte)SmcTransportProtocol.Mmio];
        }

        private byte[] GetKeyInfo(
            ReadOnlyMemory<byte> input,
            int outputBufferLength)
        {
            Assert.Equal(AppleSmcProtocol.KeyLength, input.Length);
            Assert.Equal(CrystalIdeaAppleSmcCodec.KeyInfoLength, outputBufferLength);
            KeyInfoReadCount++;

            var key = DecodeKey(input.Span);
            var register = _registers[key];
            var response = new byte[CrystalIdeaAppleSmcCodec.KeyInfoLength];
            response[0] = register.Length;
            Encoding.ASCII.GetBytes(register.Type, response.AsSpan(1, AppleSmcProtocol.KeyLength));
            response[5] = register.Attributes;
            return response;
        }

        private byte[] ReadKey(
            ReadOnlyMemory<byte> input,
            int outputBufferLength)
        {
            Assert.Equal(AppleSmcProtocol.KeyLength + 1, input.Length);
            Assert.Equal(AppleSmcProtocol.MaximumValueLength, outputBufferLength);
            KeyReadCount++;

            var request = input.Span;
            var key = DecodeKey(request[..AppleSmcProtocol.KeyLength]);
            var register = _registers[key];
            Assert.Equal(register.Length, request[AppleSmcProtocol.KeyLength]);
            return register.RawData.ToArray();
        }

        private byte[] WriteKey(
            ReadOnlyMemory<byte> input,
            int outputBufferLength)
        {
            Assert.Equal(1, outputBufferLength);
            WriteCount++;

            var request = input.Span;
            var key = DecodeKey(request[..AppleSmcProtocol.KeyLength]);
            var length = request[AppleSmcProtocol.KeyLength];
            var data = request[(AppleSmcProtocol.KeyLength + 1)..].ToArray();
            var register = _registers[key];
            Assert.Equal(register.Length, length);
            Assert.Equal(length, data.Length);
            register.RawData = data;
            return [0x46];
        }

        private static string DecodeKey(ReadOnlySpan<byte> bytes)
        {
            return Encoding.ASCII.GetString(bytes);
        }
    }

    private sealed class SmcRegister
    {
        public SmcRegister(
            string key,
            byte length,
            string type,
            byte attributes,
            ReadOnlySpan<byte> rawData)
        {
            Key = key;
            Length = length;
            Type = type;
            Attributes = attributes;
            RawData = rawData.ToArray();
        }

        public string Key { get; }

        public byte Length { get; }

        public string Type { get; }

        public byte Attributes { get; }

        public byte[] RawData { get; set; }
    }

    private sealed class InMemoryFanOverrideOwnershipStore : IFanOverrideOwnershipStore
    {
        private FanOverrideOwnershipMarker? _marker;

        public Task<FanOverrideOwnershipMarker?> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_marker);
        }

        public Task SaveNewAsync(
            FanOverrideOwnershipMarker marker,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _marker = marker;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _marker = null;
            return Task.CompletedTask;
        }
    }
}
