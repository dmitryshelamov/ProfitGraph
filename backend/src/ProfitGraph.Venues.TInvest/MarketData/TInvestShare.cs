namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     A tradable MOEX share as ProfitGraph uses it: identity, lot in shares, exact price increment and the first candles.
/// </summary>
internal sealed record TInvestShare(
    TInvestSymbol Symbol,
    string Uid,
    string Name,
    decimal Lot,
    decimal PriceIncrement,
    DateTimeOffset? FirstMinuteCandle,
    DateTimeOffset? FirstDayCandle);