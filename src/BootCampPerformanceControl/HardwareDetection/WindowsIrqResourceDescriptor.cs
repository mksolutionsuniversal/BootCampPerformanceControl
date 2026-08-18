using System.Buffers.Binary;

namespace BootCampPerformanceControl.HardwareDetection;

internal sealed record WindowsIrqResourceDescriptor(
    uint Count,
    uint Type,
    ushort Flags,
    ushort Group,
    uint AllocatedNumber,
    ulong Affinity)
{
    public const int SerializedLength = 24;

    public static bool TryParse(
        ReadOnlySpan<byte> data,
        out WindowsIrqResourceDescriptor? descriptor)
    {
        descriptor = null;

        if (data.Length < SerializedLength)
        {
            return false;
        }

        descriptor = new WindowsIrqResourceDescriptor(
            BinaryPrimitives.ReadUInt32LittleEndian(data[0..4]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[8..10]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[10..12]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[12..16]),
            BinaryPrimitives.ReadUInt64LittleEndian(data[16..24]));

        return true;
    }
}
