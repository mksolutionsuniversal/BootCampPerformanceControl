using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;

namespace BootCampPerformanceControl.FanControl;

internal sealed class AppleSmcReadOnlyFanControlService : IFanControlService
{
    private readonly FanSafetyPolicy _safetyPolicy;
    private readonly Func<CancellationToken, Task<ISmcTransport>> _openSessionAsync;

    public AppleSmcReadOnlyFanControlService()
        : this(
            new FanSafetyPolicy(),
            OpenInstalledSessionAsync)
    {
    }

    internal AppleSmcReadOnlyFanControlService(
        FanSafetyPolicy safetyPolicy,
        Func<CancellationToken, Task<ISmcTransport>> openSessionAsync)
    {
        _safetyPolicy = safetyPolicy ?? throw new ArgumentNullException(nameof(safetyPolicy));
        _openSessionAsync = openSessionAsync ?? throw new ArgumentNullException(nameof(openSessionAsync));
    }

    public async Task<FanControlStatus> ReadStatusAsync(
        string model,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var identity = _safetyPolicy.EvaluateIdentity(model);
        if (identity.Failures.Count > 0)
        {
            return FanController.CreateUnavailableStatus(identity);
        }

        await using var session = await _openSessionAsync(cancellationToken)
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

    private static async Task<ISmcTransport> OpenInstalledSessionAsync(
        CancellationToken cancellationToken)
    {
        return await CrystalIdeaAppleSmcSession
            .OpenInstalledDriverAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
