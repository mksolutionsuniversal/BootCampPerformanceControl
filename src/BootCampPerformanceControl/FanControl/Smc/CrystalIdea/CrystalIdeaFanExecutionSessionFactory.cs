using System.Runtime.ExceptionServices;
using BootCampPerformanceControl.FanControl.Smc.Windows;
using BootCampPerformanceControl.Logging;

namespace BootCampPerformanceControl.FanControl.Smc.CrystalIdea;

// Factory for a short-lived write-capable AppleSMC fan session.
// It requires AppleSMC to already be running and does not start/elevate it.
internal sealed class CrystalIdeaFanExecutionSessionFactory : IFanExecutionSessionFactory
{
    private readonly IApplicationLogger _logger;
    private readonly Func<IAppleSmcServiceController> _openServiceController;
    private readonly Func<IDeviceIoControlClient> _openDeviceClient;
    private readonly Func<IFanOverrideOwnershipStore> _openOwnershipStore;

    public CrystalIdeaFanExecutionSessionFactory(IApplicationLogger logger)
        : this(
            logger,
            static () => new WindowsAppleSmcServiceController(),
            static () => WindowsDeviceIoControlClient.OpenExclusive(
                CrystalIdeaAppleSmcTransport.DevicePath),
            () => new JsonFanOverrideOwnershipStore(logger))
    {
    }

    internal CrystalIdeaFanExecutionSessionFactory(
        IApplicationLogger logger,
        Func<IAppleSmcServiceController> openServiceController,
        Func<IDeviceIoControlClient> openDeviceClient,
        Func<IFanOverrideOwnershipStore> openOwnershipStore)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _openServiceController = openServiceController
            ?? throw new ArgumentNullException(nameof(openServiceController));
        _openDeviceClient = openDeviceClient
            ?? throw new ArgumentNullException(nameof(openDeviceClient));
        _openOwnershipStore = openOwnershipStore
            ?? throw new ArgumentNullException(nameof(openOwnershipStore));
    }

    public Task<IFanExecutionSession> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IAppleSmcServiceController? serviceController = null;
        IDeviceIoControlClient? sharedDevice = null;
        CrystalIdeaFanExecutionSession? session = null;

        try
        {
            serviceController = _openServiceController()
                ?? throw new InvalidOperationException(
                    "The AppleSMC service controller factory returned no controller.");

            var serviceState = serviceController.GetState();
            if (serviceState != AppleSmcServiceState.Running)
            {
                throw new AppleSmcServiceStateException(serviceState);
            }

            cancellationToken.ThrowIfCancellationRequested();

            sharedDevice = _openDeviceClient()
                ?? throw new InvalidOperationException(
                    "The AppleSMC device client factory returned no client.");

            var ownershipStore = _openOwnershipStore()
                ?? throw new InvalidOperationException(
                    "The fan ownership store factory returned no store.");

            session = CrystalIdeaFanExecutionSession.Create(
                serviceController,
                sharedDevice,
                ownershipStore,
                _logger);

            serviceController = null;
            sharedDevice = null;
            return Task.FromResult<IFanExecutionSession>(session);
        }
        catch (Exception operationException)
        {
            CleanupAfterOpenFailure(
                operationException,
                session,
                sharedDevice,
                serviceController);
            throw;
        }
    }

    private static void CleanupAfterOpenFailure(
        Exception operationException,
        CrystalIdeaFanExecutionSession? session,
        IDeviceIoControlClient? sharedDevice,
        IAppleSmcServiceController? serviceController)
    {
        var failures = new List<Exception> { operationException };

        if (session is not null)
        {
            try
            {
                session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
        else
        {
            if (sharedDevice is not null)
            {
                try
                {
                    sharedDevice.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (serviceController is not null)
            {
                try
                {
                    serviceController.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(operationException).Throw();
        }

        throw new AggregateException(
            "AppleSMC fan execution session setup failed and cleanup did not complete cleanly.",
            failures);
    }
}
