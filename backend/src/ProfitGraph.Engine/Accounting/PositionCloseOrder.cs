namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Reserved identity of the market order that sells a position the owner closes (ADR 0064). No graph node owns it, and its
///     tag differs from a manual order's, so listeners of manual fills do not react to it.
/// </summary>
public static class PositionCloseOrder
{
    public const string OriginNodeId = "$close";
    public const string Tag = "close";
}