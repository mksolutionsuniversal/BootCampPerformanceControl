using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.HardwareDetection;

public sealed class WindowsIoPortResourceDescriptorTests
{
    [Fact]
    public void TryParse_PhysicalAppleSmcBootDescriptor_DecodesPortRange()
    {
        var data = Convert.FromHexString(
            "000000002800000000030000000000001F0300000000000011000000");

        var parsed = WindowsIoPortResourceDescriptor.TryParse(data, out var descriptor);

        Assert.True(parsed);
        Assert.NotNull(descriptor);
        Assert.Equal(0u, descriptor.Count);
        Assert.Equal(0x28u, descriptor.Type);
        Assert.Equal(0x300UL, descriptor.AllocatedBase);
        Assert.Equal(0x31FUL, descriptor.AllocatedEnd);
        Assert.Equal(0x20UL, descriptor.Length);
        Assert.Equal(0x11u, descriptor.Flags);
    }

    [Fact]
    public void TryParse_ShortDescriptor_ReturnsFalse()
    {
        var parsed = WindowsIoPortResourceDescriptor.TryParse(
            new byte[WindowsIoPortResourceDescriptor.SerializedLength - 1],
            out var descriptor);

        Assert.False(parsed);
        Assert.Null(descriptor);
    }
}
