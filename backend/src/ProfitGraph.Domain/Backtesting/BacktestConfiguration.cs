using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Domain.Backtesting;

/// <summary>
///     Everything a run freezes when it starts (stage 3 plan, S3-5): the series and requested period against one clock
///     reading, strategy parameter values, execution settings, the holdout and what the compiled strategy was. An omitted
///     start is the venue's first candle; an omitted or later end is the latest closed candle at <see cref="ObservedAt" />.
/// </summary>
public sealed record BacktestConfiguration
{
    public CandleSeries Series { get; }
    public DateTimeOffset? From { get; }
    public DateTimeOffset? To { get; }
    public DateTimeOffset ObservedAt { get; }
    public IReadOnlyDictionary<string, decimal> Parameters { get; }
    public ExecutionSettings Settings { get; }
    public OutOfSampleSplit Split { get; }

    /// <summary>
    ///     Candles the compiled strategy needs before the start; preparation supplies as many as the venue has.
    /// </summary>
    public int WarmupCandles { get; }

    public string ExecutionPolicy { get; }
    public string Fingerprint { get; }

    public DateTimeOffset LatestClosed => Series.Timeframe.FloorToGrid(ObservedAt);

    public BacktestConfiguration(
        CandleSeries series,
        DateTimeOffset? from,
        DateTimeOffset? to,
        DateTimeOffset observedAt,
        IReadOnlyDictionary<string, decimal> parameters,
        ExecutionSettings settings,
        OutOfSampleSplit split,
        int warmupCandles,
        string executionPolicy,
        string fingerprint)
    {
        Series = series ?? throw new ArgumentNullException(nameof(series));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Split = split ?? throw new ArgumentNullException(nameof(split));
        Timeframe timeframe = series.Timeframe;
        DateTimeOffset latest = timeframe.FloorToGrid(observedAt);

        if (observedAt.Offset != TimeSpan.Zero || IsInvalidBound(from, timeframe) || IsInvalidBound(to, timeframe) || from >= latest || to <= from)
        {
            throw Invalid("The period must be UTC, on the timeframe grid, and start before the latest closed candle and before its end.");
        }

        if (warmupCandles < 0 || string.IsNullOrWhiteSpace(executionPolicy) || fingerprint is not { Length: 64 } || !fingerprint.All(char.IsAsciiHexDigit))
        {
            throw Invalid("A run needs a nonnegative warm-up, an execution policy and a SHA-256 compilation fingerprint.");
        }

        if (parameters.Keys.Any(string.IsNullOrWhiteSpace))
        {
            throw Invalid("Parameter names cannot be empty.");
        }

        From = from;
        To = to;
        ObservedAt = observedAt;
        Parameters = new SortedDictionary<string, decimal>(parameters.ToDictionary(), StringComparer.Ordinal).AsReadOnly();
        WarmupCandles = warmupCandles;
        ExecutionPolicy = executionPolicy;
        Fingerprint = fingerprint;
    }

    /// <summary>
    ///     The end of the replay range: the requested end, clamped to the latest closed candle.
    /// </summary>
    public DateTimeOffset ResolveTo()
    {
        return To is { } to && to < LatestClosed ? to : LatestClosed;
    }

    private static bool IsInvalidBound(DateTimeOffset? bound, Timeframe timeframe)
    {
        return bound is { } value && (value.Offset != TimeSpan.Zero || value < DateTimeOffset.UnixEpoch || !timeframe.IsAligned(value));
    }

    private static BacktestRunRuleException Invalid(string message)
    {
        return new BacktestRunRuleException(BacktestRunRuleException.InvalidConfiguration, message);
    }
}