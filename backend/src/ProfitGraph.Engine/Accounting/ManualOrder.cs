namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Reserved identity of an owner-submitted live market order (ADR 0091). No graph node owns it.
/// </summary>
public static class ManualOrder
{
    public const string OriginNodeId = "$manual";
    public const string Tag = "manual";
}