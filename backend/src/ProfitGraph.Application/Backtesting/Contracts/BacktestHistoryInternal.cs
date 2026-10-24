using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Backtesting.Contracts;

public sealed record BacktestHistoryInternal(
    string Identity,
    DateTimeOffset HistoryFrom,
    DateTimeOffset From,
    DateTimeOffset To,
    int WarmupCount,
    int ReplayCount,
    Guid RulesSnapshotId,
    string BaseAsset,
    string QuoteAsset,
    InstrumentRules Rules);