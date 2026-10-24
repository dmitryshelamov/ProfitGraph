namespace ProfitGraph.Api.Backtesting.Contracts;

/// <summary>
///     A replayed candle, the account after it and the facts reported with it; progress messages carry the same shape.
/// </summary>
public sealed record BacktestObservationResponse(
    long Sequence,
    DateTimeOffset OpenTime,
    int CandleIndex,
    string Phase,
    string MarketPrice,
    string Open,
    string High,
    string Low,
    string Close,
    string Volume,
    string Equity,
    string UnrealizedPnl,
    string TotalPnl,
    string RealizedPnl,
    string QuoteTotal,
    string QuoteReserved,
    string BaseTotal,
    string BaseReserved,
    string CostBasis,
    string FeesBase,
    string FeesQuote,
    string? LastBuyPrice,
    int ActiveOrders,
    IReadOnlyList<BacktestLedgerEntryResponse> Ledger,
    IReadOnlyList<BacktestDiagnosticResponse> Diagnostics);