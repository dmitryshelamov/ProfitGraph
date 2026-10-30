namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     A rule's cards in one third of a backtest's period (ADR 0071, section 5): how many closed trades and their mean R after costs,
///     empty without a trade.
/// </summary>
public sealed record ScanAutoTradeThird(int Trades, decimal? MeanR);