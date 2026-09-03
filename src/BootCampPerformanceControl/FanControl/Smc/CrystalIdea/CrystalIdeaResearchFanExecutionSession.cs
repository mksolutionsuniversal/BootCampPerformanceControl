using System.Runtime.ExceptionServices;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.FanControl.Smc.CrystalIdea;

// Research-only write-capable fan execution session. It is intentionally not
// wired into production composition; callers must opt into this factory.
internal sealed class CrystalIdeaResearchFanExecutionSession : IFanExecutionSession
{
    private readonly IAppleSmcServiceController _serviceController;
    private readonly IDeviceIoControlClient _sharedDevice;
    private readonly CrystalIdeaAppleSmcTransport _readTransport;
    private readonly CrystalIdeaResearchFanSmcWriteBackend _writeBackend;
    private readonly object _disposeSync = new();

    private Task? _disposeTask;

    private CrystalIdeaResearchFanExecutionSession(
        IAppleSmcServiceController serviceController,
        IDeviceIoControlClient sharedDevice,
        CrystalIdeaAppleSmcTransport readTransport,
        CrystalIdeaResearchFanSmcWriteBackend writeBackend,
        IFanCapabilityProbe capabilityProbe,
        IFanOverrideCoordinator overrideCoordinator)
    {
        _serviceController = serviceController;
        _sharedDevice = sharedDevice;
        _readTransport = readTransport;
        _writeBackend = writeBackend;
        CapabilityProbe = capabilityProbe;
        OverrideCoordinator = overrideCoordinator;
    }

    public IFanCapabilityProbe CapabilityProbe { get; }

    public IFanOverrideCoordinator OverrideCoordinator { get; }

    internal static CrystalIdeaResearchFanExecutionSession Create(
        IAppleSmcServiceController serviceController,
        IDeviceIoControlClient sharedDevice,
        IFanOverrideOwnershipStore ownershipStore,
        IApplicationLogger logger)
    {
        ArgumentNullException.ThrowIfNull(serviceController);
        ArgumentNullException.ThrowIfNull(sharedDevice);
        ArgumentNullException.ThrowIfNull(ownershipStore);
        ArgumentNullException.ThrowIfNull(logger);

        var readTransport = new CrystalIdeaAppleSmcTransport(
            new NonOwningDeviceIoControlClient(sharedDevice));
        var writeBackend = new CrystalIdeaResearchFanSmcWriteBackend(
            new NonOwningDeviceIoControlClient(sharedDevice));
        var protocol = new AppleSmcProtocol(readTransport);
        var safetyPolicy = new FanSafetyPolicy();
        var preflightPolicy = new FanOverridePreflightPolicy();
        var recoveryPolicy = new FanOverrideRecoveryPolicy();
        var capabilityProbe = new FanCapabilityProbe(protocol, safetyPolicy);
        var writer = new VerifiedFanOverrideWriter(
            writeBackend,
            capabilityProbe,
            preflightPolicy,
            recoveryPolicy,
            logger);
        var coordinator = new FanOverrideCoordinator(
            preflightPolicy,
            recoveryPolicy,
            ownershipStore,
            writer,
            logger);

        return new CrystalIdeaResearchFanExecutionSession(
            serviceController,
            sharedDevice,
            readTransport,
            writeBackend,
            capabilityProbe,
            coordinator);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();

        try
        {
            await _readTransport.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await _writeBackend.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            _sharedDevice.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            _serviceController.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(
                "AppleSMC fan execution session cleanup did not complete cleanly.",
                failures);
        }
    }
}
