using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Domain.Backtesting;

/// <summary>
///     What preparation fixed for a run, attached once: the captured instrument rules (not the rules that applied in the
///     past), the replay range [From, To), its warm-up and the identity of the prepared history. A market that closes (ADR
///     0023, TC1) has fewer candles than grid intervals: the closed intervals of each range are counted beside its candles.
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
    InstrumentRules Rules,
    int WarmupClosedIntervals = 0,
    int ReplayClosedIntervals = 0);