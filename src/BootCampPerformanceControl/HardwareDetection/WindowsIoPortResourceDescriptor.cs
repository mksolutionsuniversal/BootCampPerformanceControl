using System.Buffers.Binary;

namespace BootCampPerformanceControl.HardwareDetection;

internal sealed record WindowsIoPortResourceDescriptor(
    uint Count,
    uint Type,
    ulong AllocatedBase,
    ulong AllocatedEnd,
    uint Flags)
{
    public const int SerializedLength = 28;

    public ulong Length =>
        AllocatedEnd >= AllocatedBase
            ? checked(AllocatedEnd - AllocatedBase + 1)
            : 0;

    public static bool TryParse(
        ReadOnlySpan<byte> data,
        out WindowsIoPortResourceDescriptor? descriptor)
    {
        descriptor = null;

        if (data.Length < SerializedLength)
        {
            return false;
        }

        descriptor = new WindowsIoPortResourceDescriptor(
            BinaryPrimitives.ReadUInt32LittleEndian(data[0..4]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]),
            BinaryPrimitives.ReadUInt64LittleEndian(data[8..16]),
            BinaryPrimitives.ReadUInt64LittleEndian(data[16..24]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[24..28]));

        return true;
    }
}
