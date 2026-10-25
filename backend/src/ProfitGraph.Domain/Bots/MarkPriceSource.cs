namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Where a position's display price comes from: a fresh venue ticker or the last closed candle.
/// </summary>
public enum MarkPriceSource
{
    Ticker,
    Candle
}