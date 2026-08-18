namespace BootCampPerformanceControl.FanControl.Smc;

internal sealed class AppleSmcProtocol
{
    internal const int KeyLength = 4;
    internal const int MaximumValueLength = 32;

    private readonly ISmcTransport _transport;

    public AppleSmcProtocol(ISmcTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public Task<SmcTransportProtocol> GetProtocolAsync(CancellationToken cancellationToken)
    {
        return _transport.GetProtocolAsync(cancellationToken);
    }

    public async Task<SmcValue> ReadKeyAsync(
        string key,
        CancellationToken cancellationToken)
    {
        ValidateKey(key);

        var info = await _transport
            .GetKeyInfoAsync(key, cancellationToken)
            .ConfigureAwait(false);

        ValidateKeyInfo(key, info);

        var rawData = await _transport
            .ReadKeyAsync(key, info.Length, cancellationToken)
            .ConfigureAwait(false);

        if (rawData.Length != info.Length)
        {
            throw new InvalidDataException(
                $"SMC key '{key}' returned {rawData.Length} bytes but metadata declared {info.Length}.");
        }

        return new SmcValue(info, rawData.Span);
    }

    private static void ValidateKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeyLength || key.Any(character => character is < ' ' or > '~'))
        {
            throw new ArgumentException(
                "SMC keys must contain exactly four printable ASCII characters.",
                nameof(key));
        }
    }

    private static void ValidateKeyInfo(string requestedKey, SmcKeyInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (!string.Equals(info.Key, requestedKey, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"SMC metadata returned key '{info.Key}' for requested key '{requestedKey}'.");
        }

        if (info.Length is 0 or > MaximumValueLength)
        {
            throw new InvalidDataException(
                $"SMC key '{requestedKey}' reported unsupported length {info.Length}.");
        }

        if (info.Type.Length != KeyLength || info.Type.Any(character => character is < ' ' or > '~'))
        {
            throw new InvalidDataException(
                $"SMC key '{requestedKey}' reported invalid type '{info.Type}'.");
        }
    }
}
