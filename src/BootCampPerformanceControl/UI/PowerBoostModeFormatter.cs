namespace BootCampPerformanceControl.UI;

public static class PowerBoostModeFormatter
{
    public static string Format(uint value)
    {
        var description = value switch
        {
            0 => "Disabled",
            1 => "Enabled",
            2 => "Aggressive",
            3 => "Efficient Enabled",
            4 => "Efficient Aggressive",
            5 => "Aggressive At Guaranteed",
            6 => "Efficient Aggressive At Guaranteed",
            _ => "Unknown"
        };

        return $"{value} ({description})";
    }
}
