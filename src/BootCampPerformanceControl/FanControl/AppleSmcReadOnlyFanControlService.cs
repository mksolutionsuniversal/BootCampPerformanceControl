using System.ComponentModel;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.FanControl.Smc.Windows;

namespace BootCampPerformanceControl.FanControl;

internal sealed class AppleSmcReadOnlyFanControlService : IFanControlService
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;
    private const int ErrorServiceDoesNotExist = 1060;

    private readonly FanSafetyPolicy _safetyPolicy;
    private readonly Func<IAppleSmcServiceController> _openServiceController;
    private readonly IAppleSmcTransportFactory _transportFactory;

    public AppleSmcReadOnlyFanControlService()
        : this(
            new FanSafetyPolicy(),
            static () => new WindowsAppleSmcServiceController(),
            new CrystalIdeaAppleSmcTransportFactory())
    {
    }

    internal AppleSmcReadOnlyFanControlService(
        FanSafetyPolicy safetyPolicy,
        Func<IAppleSmcServiceController> openServiceController,
        IAppleSmcTransportFactory transportFactory)
    {
        _safetyPolicy = safetyPolicy ?? throw new ArgumentNullException(nameof(safetyPolicy));
        _openServiceController = openServiceController
            ?? throw new ArgumentNullException(nameof(openServiceController));
        _transportFactory = transportFactory
            ?? throw new ArgumentNullException(nameof(transportFactory));
    }

    public async Task<FanControlStatus> ReadStatusAsync(
        string model,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var identity = _safetyPolicy.EvaluateIdentity(model);
        if (identity.Failures.Count > 0)
        {
            return FanControlStatus.CreateUnavailable(
                FanBackendState.NotApplicable,
                FanSafetyState.UnsupportedModel,
                string.Join(" ", identity.Failures));
        }

        IAppleSmcServiceController? serviceController = null;

        try
        {
            serviceController = _openServiceController();
            var serviceState = serviceController.GetState();

            var unavailableStatus = CreateServiceStateStatus(serviceState);
            if (unavailableStatus is not null)
            {
                return unavailableStatus;
            }

            var sessionServiceController = serviceController;
            serviceController = null;

            await using var session = await CrystalIdeaAppleSmcSession
                .OpenReadOnlyAsync(
                    sessionServiceController,
                    _transportFactory,
                    cancellationToken)
                .ConfigureAwait(false);
            var controller = new FanController(
                new FanCapabilityProbe(
                    new AppleSmcProtocol(session),
                    _safetyPolicy));

            var result = await controller
                .ReadStatusAsync(model, cancellationToken)
                .ConfigureAwait(false);

            return result.Status;
        }
        catch (AppleSmcServiceStateException exception)
        {
            return CreateServiceStateStatus(exception.State)
                ?? throw new InvalidOperationException(
                    "A read-only AppleSMC session reported Running as an unavailable state.",
                    exception);
        }
        catch (Win32Exception exception) when (
            exception.NativeErrorCode == ErrorServiceDoesNotExist)
        {
            return FanControlStatus.CreateUnavailable(
                FanBackendState.NotInstalled,
                FanSafetyState.MonitoringUnavailable,
                "The AppleSMC compatibility service is not installed.");
        }
        catch (Win32Exception exception) when (
            exception.NativeErrorCode == ErrorSharingViolation)
        {
            return FanControlStatus.CreateUnavailable(
                FanBackendState.Busy,
                FanSafetyState.MonitoringUnavailable,
                "The AppleSMC device is in use by another application.");
        }
        catch (Win32Exception exception) when (
            exception.NativeErrorCode == ErrorAccessDenied)
        {
            return FanControlStatus.CreateUnavailable(
                FanBackendState.AccessDenied,
                FanSafetyState.MonitoringUnavailable,
                "Access to the AppleSMC compatibility backend was denied.");
        }
        finally
        {
            serviceController?.Dispose();
        }
    }

    private static FanControlStatus? CreateServiceStateStatus(
        AppleSmcServiceState serviceState)
    {
        return serviceState switch
        {
            AppleSmcServiceState.Running => null,
            AppleSmcServiceState.Stopped => FanControlStatus.CreateUnavailable(
                FanBackendState.InstalledStopped,
                FanSafetyState.MonitoringUnavailable,
                "The AppleSMC compatibility service is installed but stopped."),
            AppleSmcServiceState.StartPending or AppleSmcServiceState.StopPending =>
                FanControlStatus.CreateUnavailable(
                    FanBackendState.Transitional,
                    FanSafetyState.MonitoringUnavailable,
                    $"The AppleSMC compatibility service is in state {serviceState}."),
            _ => FanControlStatus.CreateUnavailable(
                FanBackendState.Unavailable,
                FanSafetyState.MonitoringUnavailable,
                $"The AppleSMC compatibility service is in unavailable state {serviceState}.")
        };
    }
}
