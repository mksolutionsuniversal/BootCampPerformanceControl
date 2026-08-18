using System.IO;
using System.Text;

namespace BootCampPerformanceControl.FanControl.Smc.CrystalIdea;

internal static class CrystalIdeaAppleSmcCodec
{
    internal const int KeyInfoLength = 6;

    public static byte[] EncodeKey(string key)
    {
        ValidateKey(key);
        return Encoding.ASCII.GetBytes(key);
    }

    public static byte[] BuildReadKeyRequest(string key, byte length)
    {
        if (length is 0 or > AppleSmcProtocol.MaximumValueLength)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var request = new byte[AppleSmcProtocol.KeyLength + 1];
        EncodeKey(key).CopyTo(request, 0);
        request[^1] = length;
        return request;
    }

    public static SmcTransportProtocol ParseProtocol(ReadOnlySpan<byte> response)
    {
        if (response.Length != 1)
        {
            throw new InvalidDataException(
                $"GET_PROTOCOL returned {response.Length} bytes; expected 1.");
        }

        return (SmcTransportProtocol)response[0];
    }

    public static SmcKeyInfo ParseKeyInfo(string key, ReadOnlySpan<byte> response)
    {
        ValidateKey(key);

        if (response.Length != KeyInfoLength)
        {
            throw new InvalidDataException(
                $"GET_KEY_INFO for '{key}' returned {response.Length} bytes; expected {KeyInfoLength}.");
        }

        var type = Encoding.ASCII.GetString(response.Slice(1, AppleSmcProtocol.KeyLength));
        return new SmcKeyInfo(
            key,
            response[0],
            type,
            response[5]);
    }

    private static void ValidateKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != AppleSmcProtocol.KeyLength ||
            key.Any(character => character is < ' ' or > '~'))
        {
            throw new ArgumentException(
                "SMC keys must contain exactly four printable ASCII characters.",
                nameof(key));
        }
    }
}
