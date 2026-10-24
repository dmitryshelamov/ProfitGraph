using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Публичные правила инструмента, снятые при подготовке прогона; это не исторически точные правила биржи.
/// </summary>
public sealed record BacktestMarketSnapshot(
    string Source,
    string Market,
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    Guid RulesSnapshotId,
    DateTimeOffset RulesCapturedAt,
    decimal BasePrecisionIncrement,
    decimal QuotePrecisionIncrement,
    InstrumentRules Rules);