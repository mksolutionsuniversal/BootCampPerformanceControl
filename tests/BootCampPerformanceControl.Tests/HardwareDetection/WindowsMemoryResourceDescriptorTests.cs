using System.Buffers.Binary;
using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.HardwareDetection;

public sealed class WindowsMemoryResourceDescriptorTests
{
    [Fact]
    public void TryParse_ValidDescriptor_DecodesAllocatedRange()
    {
        var data = new byte[WindowsMemoryResourceDescriptor.SerializedLength];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8, 8), 0x00000000FE000000UL);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(16, 8), 0x00000000FE000FFFUL);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24, 4), 0x0B);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28, 4), 0);

        var parsed = WindowsMemoryResourceDescriptor.TryParse(data, out var descriptor);

        Assert.True(parsed);
        Assert.NotNull(descriptor);
        Assert.Equal(0u, descriptor.Count);
        Assert.Equal(1u, descriptor.Type);
        Assert.Equal(0x00000000FE000000UL, descriptor.AllocatedBase);
        Assert.Equal(0x00000000FE000FFFUL, descriptor.AllocatedEnd);
        Assert.Equal(0x1000UL, descriptor.Length);
        Assert.Equal(0x0Bu, descriptor.Flags);
        Assert.Equal(0u, descriptor.Reserved);
    }

    [Fact]
    public void TryParse_ShortDescriptor_ReturnsFalse()
    {
        var parsed = WindowsMemoryResourceDescriptor.TryParse(
            new byte[WindowsMemoryResourceDescriptor.SerializedLength - 1],
            out var descriptor);

        Assert.False(parsed);
        Assert.Null(descriptor);
    }

    [Fact]
    public void Length_ReversedRange_ReturnsZero()
    {
        var descriptor = new WindowsMemoryResourceDescriptor(
            Count: 0,
            Type: 1,
            AllocatedBase: 0x2000,
            AllocatedEnd: 0x1000,
            Flags: 0,
            Reserved: 0);

        Assert.Equal(0UL, descriptor.Length);
    }
}
