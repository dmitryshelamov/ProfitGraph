namespace ProfitGraph.Application.Backtesting.Contracts;

/// <summary>
///     Consecutive replayed candles [<paramref name="FirstSequence" />, <paramref name="LastSequence" />] as one point.
/// </summary>
public sealed record BacktestChartPointInternal(
    long FirstSequence,
    long LastSequence,
    DateTimeOffset FirstOpenTime,
    DateTimeOffset LastOpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal Equity,
    decimal MinEquity,
    decimal MaxEquity,
    decimal TotalPnl,
    int BuyFills,
    int SellFills);