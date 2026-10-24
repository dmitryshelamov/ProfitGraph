namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record BacktestChartPointResponse(
    long FirstSequence,
    long LastSequence,
    DateTimeOffset FirstOpenTime,
    DateTimeOffset LastOpenTime,
    string Open,
    string High,
    string Low,
    string Close,
    string Volume,
    string Equity,
    string MinEquity,
    string MaxEquity,
    string TotalPnl,
    int BuyFills,
    int SellFills);