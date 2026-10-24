using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Domain.Backtesting;

/// <summary>
///     What preparation fixed for a run, attached once: the captured instrument rules (not the rules that applied in the
///     past), the replay range [From, To), its warm-up and the identity of the prepared history.
/// </summary>
public sealed record BacktestHistoryInfo(
    string Identity,
    DateTimeOffset HistoryFrom,
    DateTimeOffset From,
    DateTimeOffset To,
    int WarmupCount,
    int ReplayCount,
    InstrumentRulesSnapshotId RulesSnapshotId,
    string BaseAsset,
    string QuoteAsset,
    InstrumentRules Rules);