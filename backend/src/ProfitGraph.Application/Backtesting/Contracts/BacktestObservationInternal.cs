namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     A replayed candle, the account after it and the facts reported with it; observations are numbered from 1 and are
///     what progress messages carry.
/// </summary>
public sealed record BacktestObservationInternal(
    long Sequence,
    DateTimeOffset OpenTime,
    int CandleIndex,
    string Phase,
    decimal MarketPrice,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal Equity,
    decimal UnrealizedPnl,
    decimal TotalPnl,
    decimal RealizedPnl,
    decimal QuoteTotal,
    decimal QuoteReserved,
    decimal BaseTotal,
    decimal BaseReserved,
    decimal CostBasis,
    decimal FeesBase,
    decimal FeesQuote,
    decimal? LastBuyPrice,
    int ActiveOrders,
    IReadOnlyList<BacktestLedgerEntryInternal> Ledger,
    IReadOnlyList<BacktestDiagnosticInternal> Diagnostics);