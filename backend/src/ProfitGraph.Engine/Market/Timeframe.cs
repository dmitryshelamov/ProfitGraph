namespace ProfitGraph.Engine.Market;

/// <summary>
///     Фиксированный интервал свечи; месячные интервалы не поддерживаются из-за переменной длительности.
/// </summary>
public sealed record Timeframe
{
    public string Key { get; }
    public TimeSpan Duration { get; }

    private Timeframe(string key, TimeSpan duration)
    {
        Key = key;
        Duration = duration;
    }

    public static Timeframe Parse(string key)
    {
        return key switch
        {
            "1m" => new Timeframe(key, TimeSpan.FromMinutes(1)),
            "3m" => new Timeframe(key, TimeSpan.FromMinutes(3)),
            "5m" => new Timeframe(key, TimeSpan.FromMinutes(5)),
            "15m" => new Timeframe(key, TimeSpan.FromMinutes(15)),
            "30m" => new Timeframe(key, TimeSpan.FromMinutes(30)),
            "1h" => new Timeframe(key, TimeSpan.FromHours(1)),
            "2h" => new Timeframe(key, TimeSpan.FromHours(2)),
            "4h" => new Timeframe(key, TimeSpan.FromHours(4)),
            "6h" => new Timeframe(key, TimeSpan.FromHours(6)),
            "12h" => new Timeframe(key, TimeSpan.FromHours(12)),
            "1d" => new Timeframe(key, TimeSpan.FromDays(1)),
            _ => throw new ArgumentException("Unsupported timeframe.", nameof(key))
        };
    }

    public bool IsAligned(DateTimeOffset time)
    {
        return (time.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) % Duration.Ticks == 0;
    }

    /// <summary>
    ///     Округляет момент вниз до UTC-сетки таймфрейма: для текущего времени это граница последней закрытой свечи.
    /// </summary>
    public DateTimeOffset FloorToGrid(DateTimeOffset time)
    {
        long ticks = time.UtcTicks - (time.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) % Duration.Ticks;

        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}