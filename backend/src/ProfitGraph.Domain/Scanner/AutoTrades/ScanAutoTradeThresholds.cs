namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     What a rule must show before it trades (ADR 0071, sections 5 and 7): the closed trades and the mean R of each third of a backtest,
///     and the closed paper trades before live trading.
/// </summary>
public sealed record ScanAutoTradeThresholds(int MinBacktestTrades, decimal MinBacktestMeanR, int MinPaperTrades);