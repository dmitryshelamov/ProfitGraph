namespace ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

/// <summary>
///     A backtest of a rule's proof as stored: its id, the trades and mean R of each third and the move of the reference pair.
/// </summary>
internal sealed record ScanAutoTradeEvidenceDb(Guid BacktestId, int[] Trades, decimal?[] MeanR, decimal? MarketChange);