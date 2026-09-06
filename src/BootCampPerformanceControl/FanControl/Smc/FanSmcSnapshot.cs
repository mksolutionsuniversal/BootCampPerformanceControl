namespace BootCampPerformanceControl.FanControl.Smc;

internal enum SmcKeyObservationState
{
    Available,
    ConfirmedAbsent,
    ReadFailed
}

internal sealed record SmcKeyObservation
{
    private SmcKeyObservation(
        string key,
        SmcKeyObservationState state,
        SmcValue? value,
        string? failure)
    {
        Key = key;
        State = state;
        Value = value;
        Failure = failure;
    }

    public string Key { get; }

    public SmcKeyObservationState State { get; }

    public SmcValue? Value { get; }

    public string? Failure { get; }

    public static SmcKeyObservation Available(SmcValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new SmcKeyObservation(
            value.Info.Key,
            SmcKeyObservationState.Available,
            value,
            null);
    }

    public static SmcKeyObservation ConfirmedAbsent(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new SmcKeyObservation(
            key,
            SmcKeyObservationState.ConfirmedAbsent,
            null,
            null);
    }

    public static SmcKeyObservation ReadFailed(string key, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(exception);
        return new SmcKeyObservation(
            key,
            SmcKeyObservationState.ReadFailed,
            null,
            $"{exception.GetType().Name}: {exception.Message}");
    }

}

internal sealed record FanSmcChannelSnapshot
{
    public FanSmcChannelSnapshot(
        FanIndex index,
        SmcValue maximum,
        SmcValue actual,
        SmcValue mode,
        SmcValue target)
        : this(
            index,
            SmcKeyObservation.ReadFailed(
                index.GetSmcKey("Mn"),
                new InvalidOperationException("Key was not probed.")),
            SmcKeyObservation.Available(maximum),
            SmcKeyObservation.Available(actual),
            SmcKeyObservation.Available(mode),
            SmcKeyObservation.Available(target))
    {
    }

    public FanSmcChannelSnapshot(
        FanIndex index,
        SmcKeyObservation minimum,
        SmcKeyObservation maximum,
        SmcKeyObservation actual,
        SmcKeyObservation mode,
        SmcKeyObservation target)
    {
        Index = index;
        Minimum = minimum ?? throw new ArgumentNullException(nameof(minimum));
        MaximumObservation = maximum ?? throw new ArgumentNullException(nameof(maximum));
        ActualObservation = actual ?? throw new ArgumentNullException(nameof(actual));
        ModeObservation = mode ?? throw new ArgumentNullException(nameof(mode));
        TargetObservation = target ?? throw new ArgumentNullException(nameof(target));
    }

    public FanIndex Index { get; }

    public SmcKeyObservation Minimum { get; }

    public SmcKeyObservation MaximumObservation { get; }

    public SmcKeyObservation ActualObservation { get; }

    public SmcKeyObservation ModeObservation { get; }

    public SmcKeyObservation TargetObservation { get; }

    public SmcValue Maximum => MaximumObservation.Value
        ?? throw new InvalidOperationException($"SMC key '{MaximumObservation.Key}' is unavailable.");

    public SmcValue Actual => ActualObservation.Value
        ?? throw new InvalidOperationException($"SMC key '{ActualObservation.Key}' is unavailable.");

    public SmcValue? Mode => ModeObservation.Value;

    public SmcValue Target => TargetObservation.Value
        ?? throw new InvalidOperationException($"SMC key '{TargetObservation.Key}' is unavailable.");

    public IReadOnlyList<SmcKeyObservation> Observations =>
        [Minimum, MaximumObservation, ActualObservation, TargetObservation, ModeObservation];
}

internal sealed record FanSmcSnapshot
{
    public FanSmcSnapshot(
        SmcValue fanCount,
        IEnumerable<FanSmcChannelSnapshot> fans,
        SmcKeyObservation? globalMode = null)
    {
        FanCount = fanCount ?? throw new ArgumentNullException(nameof(fanCount));
        ArgumentNullException.ThrowIfNull(fans);
        Fans = fans.ToArray();
        GlobalMode = globalMode ?? SmcKeyObservation.ReadFailed(
            "FS! ",
            new InvalidOperationException("Key was not probed."));
    }

    public SmcValue FanCount { get; }

    public IReadOnlyList<FanSmcChannelSnapshot> Fans { get; }

    public SmcKeyObservation GlobalMode { get; }

    public IReadOnlyList<SmcValue> Values =>
        [FanCount, .. Fans.SelectMany(fan => fan.Observations)
            .Select(observation => observation.Value)
            .Where(value => value is not null)
            .Cast<SmcValue>(), .. GlobalMode.Value is null ? [] : new[] { GlobalMode.Value }];
}
