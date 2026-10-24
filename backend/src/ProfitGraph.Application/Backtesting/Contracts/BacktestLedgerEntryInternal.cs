namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     A committed ledger fact with the market cursor it happened at.
/// </summary>
public sealed record BacktestLedgerEntryInternal(
    long Sequence,
    string Kind,
    int CandleIndex,
    DateTimeOffset CandleOpenTime,
    string Phase,
    decimal MarketPrice,
    BacktestOrderInternal? Order,
    BacktestFillInternal? Fill,
    BacktestRejectionInternal? Rejection);