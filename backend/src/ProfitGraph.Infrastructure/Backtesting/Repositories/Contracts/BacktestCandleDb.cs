namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

/// <summary>
///     A <c>backtest_candles</c> row: the replayed candle and the account after it.
/// </summary>
internal sealed class BacktestCandleDb
{
    public long Sequence { get; init; }
    public DateTimeOffset OpenTime { get; init; }
    public int CandleIndex { get; init; }
    public string Phase { get; init; } = null!;
    public decimal MarketPrice { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public decimal Equity { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public decimal TotalPnl { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal QuoteTotal { get; init; }
    public decimal QuoteReserved { get; init; }
    public decimal BaseTotal { get; init; }
    public decimal BaseReserved { get; init; }
    public decimal CostBasis { get; init; }
    public decimal FeesBase { get; init; }
    public decimal FeesQuote { get; init; }
    public decimal? LastBuyPrice { get; init; }
    public int ActiveOrders { get; init; }
    public long LedgerFrom { get; init; }
    public long LedgerThrough { get; init; }
    public long DiagnosticFrom { get; init; }
    public long DiagnosticThrough { get; init; }
}