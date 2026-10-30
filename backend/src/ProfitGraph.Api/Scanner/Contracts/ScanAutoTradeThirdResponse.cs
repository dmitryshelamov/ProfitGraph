namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     Closed card trades in a third of a backtest's period and their mean R, an exact string, null without a trade.
/// </summary>
public sealed record ScanAutoTradeThirdResponse(int Trades, string? MeanR);