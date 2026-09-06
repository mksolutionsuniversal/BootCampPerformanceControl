using System.Diagnostics;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.GlobalMaskFpe2Qualification;

internal sealed class PhysicalQualificationSession : IGlobalMaskQualificationSession
{
    private readonly IAppleSmcServiceController _serviceController;
    private readonly IDeviceIoControlClient _sharedDevice;
    private readonly CrystalIdeaAppleSmcTransport _readTransport;
    private readonly CrystalIdeaFanSmcWriteBackend _writeBackend;
    private readonly AppleSmcProtocol _protocol;
    private readonly FanCapabilityProbe _probe;

    private PhysicalQualificationSession(
        IAppleSmcServiceController serviceController,
        IDeviceIoControlClient sharedDevice,
        CrystalIdeaAppleSmcTransport readTransport,
        CrystalIdeaFanSmcWriteBackend writeBackend)
    {
        _serviceController = serviceController;
        _sharedDevice = sharedDevice;
        _readTransport = readTransport;
        _writeBackend = writeBackend;
        _protocol = new AppleSmcProtocol(readTransport);
        _probe = new FanCapabilityProbe(_protocol, new FanSafetyPolicy());
    }

    public static PhysicalQualificationSession OpenAlreadyRunning()
    {
        IAppleSmcServiceController? serviceController = null;
        IDeviceIoControlClient? sharedDevice = null;
        CrystalIdeaAppleSmcTransport? readTransport = null;
        CrystalIdeaFanSmcWriteBackend? writeBackend = null;

        try
        {
            serviceController = new WindowsAppleSmcServiceController();
            var state = serviceController.GetState();
            if (state != AppleSmcServiceState.Running)
            {
                throw new AppleSmcServiceStateException(state);
            }

            sharedDevice = WindowsDeviceIoControlClient.OpenExclusive(
                CrystalIdeaAppleSmcTransport.DevicePath);
            readTransport = new CrystalIdeaAppleSmcTransport(
                new NonOwningDeviceIoControlClient(sharedDevice));
            writeBackend = new CrystalIdeaFanSmcWriteBackend(
                new NonOwningDeviceIoControlClient(sharedDevice));

            var session = new PhysicalQualificationSession(
                serviceController,
                sharedDevice,
                readTransport,
                writeBackend);
            serviceController = null;
            sharedDevice = null;
            readTransport = null;
            writeBackend = null;
            return session;
        }
        catch
        {
            writeBackend?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            readTransport?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            sharedDevice?.Dispose();
            serviceController?.Dispose();
            throw;
        }
    }

    public AppleSmcServiceState GetServiceState() => _serviceController.GetState();

    public Task<FanControlCapabilityResult> ProbeAsync(
        string model,
        CancellationToken cancellationToken) =>
        _probe.ProbeAsync(model, cancellationToken);

    public Task<SmcValue> ReadKeyAsync(
        string key,
        CancellationToken cancellationToken) =>
        _protocol.ReadKeyAsync(key, cancellationToken);

    public Task WriteKeyAsync(
        string key,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (string.Equals(key, "FS! ", StringComparison.Ordinal) &&
            payload.Span.SequenceEqual(new byte[] { 0x00, 0x01 }))
        {
            return _writeBackend.SetGlobalManualMaskAsync(0x0001, cancellationToken);
        }

        if (string.Equals(key, "FS! ", StringComparison.Ordinal) &&
            payload.Span.SequenceEqual(new byte[] { 0x00, 0x00 }))
        {
            return _writeBackend.SetGlobalAppleAutoAsync(cancellationToken);
        }

        if (string.Equals(key, "F0Tg", StringComparison.Ordinal) && payload.Length == 2)
        {
            return _writeBackend.SetFpe2TargetPayloadAsync(
                new FanIndex(0),
                payload,
                cancellationToken);
        }

        throw new InvalidOperationException(
            $"Physical qualification session forbids SMC write key/payload '{key}'/{Convert.ToHexString(payload.Span)}.");
    }

    public async ValueTask DisposeAsync()
    {
        Exception? failure = null;
        try
        {
            await _writeBackend.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await _readTransport.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException(failure, exception);
        }

        try
        {
            _sharedDevice.Dispose();
            _serviceController.Dispose();
        }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException(failure, exception);
        }

        if (failure is not null)
        {
            throw failure;
        }
    }

    private sealed class NonOwningDeviceIoControlClient : IDeviceIoControlClient
    {
        private readonly IDeviceIoControlClient _inner;

        public NonOwningDeviceIoControlClient(IDeviceIoControlClient inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public byte[] Invoke(
            uint controlCode,
            ReadOnlyMemory<byte> input,
            int outputBufferLength) =>
            _inner.Invoke(controlCode, input, outputBufferLength);

        public void Dispose()
        {
        }
    }
}

internal static class QualificationProcessConflictDetector
{
    private static readonly HashSet<string> BlockingProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "BootCampPerformanceControl",
            "MacsFanControl"
        };

    public static IReadOnlyList<QualificationProcessConflict> Find()
    {
        var conflicts = new List<QualificationProcessConflict>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (BlockingProcessNames.Contains(process.ProcessName))
                    {
                        conflicts.Add(new QualificationProcessConflict(
                            process.ProcessName,
                            process.Id));
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        return conflicts;
    }
}
