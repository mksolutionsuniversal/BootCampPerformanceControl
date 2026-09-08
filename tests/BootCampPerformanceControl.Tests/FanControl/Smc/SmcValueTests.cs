using BootCampPerformanceControl.FanControl.Smc;

namespace BootCampPerformanceControl.Tests.FanControl.Smc;

public sealed class SmcValueTests
{
    [Theory]
    [InlineData(0x60, 0xDC, 6199f)]
    [InlineData(0x14, 0x4C, 1299f)]
    [InlineData(0x24, 0x04, 2305f)]
    [InlineData(0x24, 0x8C, 2339f)]
    public void GetFpe2_DecodesUnsignedBigEndianWithTwoFractionalBits(
        byte high,
        byte low,
        float expectedRpm)
    {
        var value = new SmcValue(
            new SmcKeyInfo("F0Mx", 2, "fpe2", 0xC0),
            [high, low]);

        Assert.Equal(expectedRpm, value.GetFpe2());
    }
}
