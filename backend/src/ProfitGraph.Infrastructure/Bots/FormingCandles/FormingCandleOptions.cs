namespace ProfitGraph.Infrastructure.Bots.FormingCandles;

/// <summary>
///     Section <c>Bots:FormingCandles</c>: how a forming-candle feed paces what it passes on and how it recovers (ADR 0026): at most one update of the forming
///     candle per second, a silent feed reported as without data after 90 s, and reconnecting after 1 s, doubling up to 1 min.
/// </summary>
internal sealed class FormingCandleOptions
{
    internal const string SectionName = "Bots:FormingCandles";

    public TimeSpan Throttle { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan SilenceAfter { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(1);

    public bool IsValid()
    {
        return Throttle >= TimeSpan.Zero && SilenceAfter > TimeSpan.Zero && InitialBackoff > TimeSpan.Zero && MaxBackoff >= InitialBackoff;
    }

    public TimeSpan Backoff(int failures)
    {
        double scaled = InitialBackoff.TotalSeconds * Math.Pow(x: 2, Math.Min(failures, val2: 16));

        return scaled >= MaxBackoff.TotalSeconds ? MaxBackoff : TimeSpan.FromSeconds(scaled);
    }
}