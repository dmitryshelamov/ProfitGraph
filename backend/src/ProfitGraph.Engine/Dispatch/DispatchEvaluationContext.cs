using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Frozen account, instrument and execution inputs used for a single dispatch projection.
/// </summary>
public sealed record DispatchEvaluationContext(
    AccountSnapshot Account,
    InstrumentRules Rules,
    ExecutionSettings Settings,
    ReachedMarketContext Market,
    DateTimeOffset? PositionOpenedAt = null);