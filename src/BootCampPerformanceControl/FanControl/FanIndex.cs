namespace BootCampPerformanceControl.FanControl;

internal readonly record struct FanIndex
{
    public const int MaximumRepresentableValue = 9;

    public FanIndex(int value)
    {
        if (value is < 0 or > MaximumRepresentableValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Fan indexes must be single decimal digits from 0 to {MaximumRepresentableValue}.");
        }

        Value = value;
    }

    public int Value { get; }

    public static explicit operator FanIndex(int value) => new(value);

    public string GetSmcKey(string suffix)
    {
        if (suffix is null || suffix.Length != 2 ||
            suffix.Any(character => character is < ' ' or > '~'))
        {
            throw new ArgumentException(
                "An indexed fan SMC key suffix must contain exactly two printable ASCII characters.",
                nameof(suffix));
        }

        return $"F{Value}{suffix}";
    }

    public override string ToString() => $"Fan{Value}";
}
