using BootCampPerformanceControl.FanControl.Smc;

namespace BootCampPerformanceControl.FanControl;

internal sealed class FanCapabilityProbe
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
        var modelGate = _safetyPolicy.EvaluateIdentity(model);
        if (modelGate.Failures.Count > 0)
        {
            return modelGate;
        }

        var transportProtocol = await _protocol
            .GetProtocolAsync(cancellationToken)
            .ConfigureAwait(false);

        var protocolGate = _safetyPolicy.EvaluateIdentity(model, transportProtocol);
        if (protocolGate.Failures.Count > 0)
        {
            return protocolGate;
        }

        var snapshot = new FanSmcSnapshot(
            await _protocol.ReadKeyAsync("FNum", cancellationToken).ConfigureAwait(false),
            await _protocol.ReadKeyAsync("F0Mx", cancellationToken).ConfigureAwait(false),
            await _protocol.ReadKeyAsync("F1Mx", cancellationToken).ConfigureAwait(false),
            await _protocol.ReadKeyAsync("F0Ac", cancellationToken).ConfigureAwait(false),
            await _protocol.ReadKeyAsync("F1Ac", cancellationToken).ConfigureAwait(false),
            await _protocol.ReadKeyAsync("F0Md", cancellationToken).ConfigureAwait(false),
            await _protocol.ReadKeyAsync("F1Md", cancellationToken).ConfigureAwait(false),
            await _protocol.ReadKeyAsync("F0Tg", cancellationToken).ConfigureAwait(false),
            await _protocol.ReadKeyAsync("F1Tg", cancellationToken).ConfigureAwait(false));

        return _safetyPolicy.Evaluate(model, transportProtocol, snapshot);
    }
}
