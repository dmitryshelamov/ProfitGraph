using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     A grid started or stopped (ADR 0058); the anchor is the start's price and null for a stop.
/// </summary>
public sealed record GridPayload(string OriginNodeId, decimal? Anchor) : EventPayload
{
    public override EventPayloadType PayloadType => EventPayloadType.Grid;
}