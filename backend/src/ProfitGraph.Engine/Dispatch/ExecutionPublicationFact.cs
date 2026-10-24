using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Actual execution result retained even if the generated lifecycle event exceeds the cascade budget.
/// </summary>
public sealed record ExecutionPublicationFact(OrderSnapshot Order, OrderExecution Result, ReachedMarketContext Market);