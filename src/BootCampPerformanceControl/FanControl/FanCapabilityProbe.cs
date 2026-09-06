using BootCampPerformanceControl.FanControl.Smc;

namespace BootCampPerformanceControl.FanControl;

internal sealed class FanCapabilityProbe : IFanCapabilityProbe
{
    private readonly AppleSmcProtocol _protocol;
    private readonly FanSafetyPolicy _safetyPolicy;

    public FanCapabilityProbe(
        AppleSmcProtocol protocol,
        FanSafetyPolicy safetyPolicy)
    {
        _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        _safetyPolicy = safetyPolicy ?? throw new ArgumentNullException(nameof(safetyPolicy));
    }

    public async Task<FanControlCapabilityResult> ProbeAsync(
        string model,
        CancellationToken cancellationToken)
    {
        var transportProtocol = await _protocol
            .GetProtocolAsync(cancellationToken)
            .ConfigureAwait(false);

        var protocolGate = _safetyPolicy.EvaluateIdentity(model, transportProtocol);
        if (protocolGate.Failures.Count > 0)
        {
            return protocolGate;
        }

        var fanCountValue = await _protocol
            .ReadKeyAsync("FNum", cancellationToken)
            .ConfigureAwait(false);

        if (!_safetyPolicy.TryDecodeFanCount(fanCountValue, out var fanCount, out var failure))
        {
            return new FanControlCapabilityResult(
                IsReadSupported: false,
                IsHardwareSafetyGateSatisfied: false,
                [failure],
                transportProtocol,
                new FanSmcSnapshot(fanCountValue, Array.Empty<FanSmcChannelSnapshot>()),
                FanCapabilityFamily.Unknown);
        }

        if (fanCount == 0)
        {
            return _safetyPolicy.Evaluate(
                model,
                transportProtocol,
                new FanSmcSnapshot(fanCountValue, []));
        }

        var fans = new List<FanSmcChannelSnapshot>(fanCount);
        for (var value = 0; value < fanCount; value++)
        {
            var index = new FanIndex(value);
            fans.Add(new FanSmcChannelSnapshot(
                index,
                await ProbeOptionalAsync(index.GetSmcKey("Mn"), cancellationToken).ConfigureAwait(false),
                await ProbeOptionalAsync(index.GetSmcKey("Mx"), cancellationToken).ConfigureAwait(false),
                await ProbeOptionalAsync(index.GetSmcKey("Ac"), cancellationToken).ConfigureAwait(false),
                await ProbeOptionalAsync(index.GetSmcKey("Md"), cancellationToken).ConfigureAwait(false),
                await ProbeOptionalAsync(index.GetSmcKey("Tg"), cancellationToken).ConfigureAwait(false)));
        }

        var snapshot = new FanSmcSnapshot(
            fanCountValue,
            fans,
            await ProbeOptionalAsync("FS! ", cancellationToken).ConfigureAwait(false));

        return _safetyPolicy.Evaluate(model, transportProtocol, snapshot);
    }

    private async Task<SmcKeyObservation> ProbeOptionalAsync(
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            return SmcKeyObservation.Available(
                await _protocol.ReadKeyAsync(key, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return SmcKeyObservation.Unavailable(key, exception);
        }
    }
}
