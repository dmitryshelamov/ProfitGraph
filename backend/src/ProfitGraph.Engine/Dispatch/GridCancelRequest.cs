namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     A cancel block named a grid (ADR 0058, section 7): the grid cancels its orders and stops without selling.
/// </summary>
public sealed record GridCancelRequest(string NodeId, string GridNodeId) : ActionRequest(NodeId);