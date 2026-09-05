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
                new FanSmcSnapshot(fanCountValue, Array.Empty<FanSmcChannelSnapshot>()));
        }

        var fans = new List<FanSmcChannelSnapshot>(fanCount);
        for (var value = 0; value < fanCount; value++)
        {
            var index = new FanIndex(value);
            fans.Add(new FanSmcChannelSnapshot(
                index,
                await _protocol.ReadKeyAsync(index.GetSmcKey("Mx"), cancellationToken).ConfigureAwait(false),
                await _protocol.ReadKeyAsync(index.GetSmcKey("Ac"), cancellationToken).ConfigureAwait(false),
                await _protocol.ReadKeyAsync(index.GetSmcKey("Md"), cancellationToken).ConfigureAwait(false),
                await _protocol.ReadKeyAsync(index.GetSmcKey("Tg"), cancellationToken).ConfigureAwait(false)));
        }

        var snapshot = new FanSmcSnapshot(fanCountValue, fans);

        return _safetyPolicy.Evaluate(model, transportProtocol, snapshot);
    }
}
