using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Exact order submission request; acceptance reserves without executing.
/// </summary>
public sealed record SubmitActionRequest(OrderRequest Order) : ActionRequest(Order.OriginNodeId);