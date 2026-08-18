using System.Buffers.Binary;

namespace BootCampPerformanceControl.FanControl.Smc;

internal sealed class SmcValue
{
    private readonly byte[] _rawData;

    public SmcValue(SmcKeyInfo info, ReadOnlySpan<byte> rawData)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        _rawData = rawData.ToArray();
    }

    public SmcKeyInfo Info { get; }

    public ReadOnlyMemory<byte> RawData => _rawData;

    public byte GetUInt8()
    {
        EnsureTypeAndLength("ui8 ", 1);
        return _rawData[0];
    }

    public float GetFloat32()
    {
        EnsureTypeAndLength("flt ", 4);
        var bits = BinaryPrimitives.ReadInt32LittleEndian(_rawData);
        return BitConverter.Int32BitsToSingle(bits);
    }

    private void EnsureTypeAndLength(string expectedType, int expectedLength)
    {
        if (!string.Equals(Info.Type, expectedType, StringComparison.Ordinal) ||
            _rawData.Length != expectedLength ||
            Info.Length != expectedLength)
        {
            throw new InvalidOperationException(
                $"SMC key '{Info.Key}' cannot be decoded as '{expectedType}'. " +
                $"Metadata is type '{Info.Type}', length {Info.Length}; raw length is {_rawData.Length}.");
        }
    }
}
