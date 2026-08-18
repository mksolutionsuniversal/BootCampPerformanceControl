using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.HardwareDetection;

public sealed class WindowsIrqResourceDescriptorTests
{
    [Fact]
    public void TryParse_PhysicalAppleSmcBootDescriptor_DecodesIrq()
    {
        var data = Convert.FromHexString(
            "000000000C0000000200000006000000FFFFFFFF00000000");

        var parsed = WindowsIrqResourceDescriptor.TryParse(data, out var descriptor);

        Assert.True(parsed);
        Assert.NotNull(descriptor);
        Assert.Equal(0u, descriptor.Count);
        Assert.Equal(0x0Cu, descriptor.Type);
        Assert.Equal((ushort)0x0002, descriptor.Flags);
        Assert.Equal((ushort)0, descriptor.Group);
        Assert.Equal(6u, descriptor.AllocatedNumber);
        Assert.Equal(0x00000000FFFFFFFFUL, descriptor.Affinity);
    }

    [Fact]
    public void TryParse_ShortDescriptor_ReturnsFalse()
    {
        var parsed = WindowsIrqResourceDescriptor.TryParse(
            new byte[WindowsIrqResourceDescriptor.SerializedLength - 1],
            out var descriptor);

        Assert.False(parsed);
        Assert.Null(descriptor);
    }
}
