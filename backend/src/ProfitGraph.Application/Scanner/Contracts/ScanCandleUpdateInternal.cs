using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A 1-minute candle of one instrument from a scan stream: closed, or still forming when <see cref="Closed" /> is false.
/// </summary>
public sealed record ScanCandleUpdateInternal(string Symbol, ScanCandle Candle, bool Closed)
{
    /// <summary>
    ///     Carries no candle: the connection is subscribed and alive while the venue sends none (ADR 0062).
    /// </summary>
    public static ScanCandleUpdateInternal Alive { get; } = new ScanCandleUpdateInternal(
        string.Empty,
        new ScanCandle(DateTimeOffset.UnixEpoch, Open: 1m, High: 1m, Low: 1m, Close: 1m, Volume: 0m, Turnover: 0m),
        Closed: false);

    public bool IsAlive => ReferenceEquals(this, Alive);
}