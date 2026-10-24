namespace ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;

internal sealed class BacktestChartPointDb
{
    public long FirstSequence { get; init; }
    public long LastSequence { get; init; }
    public DateTimeOffset FirstOpenTime { get; init; }
    public DateTimeOffset LastOpenTime { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public decimal Equity { get; init; }
    public decimal MinEquity { get; init; }
    public decimal MaxEquity { get; init; }
    public decimal TotalPnl { get; init; }
    public int BuyFills { get; init; }
    public int SellFills { get; init; }
}