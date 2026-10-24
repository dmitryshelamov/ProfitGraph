namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record BacktestHistoryResponse(
    string Identity,
    DateTimeOffset HistoryFrom,
    DateTimeOffset From,
    DateTimeOffset To,
    int WarmupCount,
    int ReplayCount,
    Guid RulesSnapshotId,
    string BaseAsset,
    string QuoteAsset,
    InstrumentRulesResponse Rules);