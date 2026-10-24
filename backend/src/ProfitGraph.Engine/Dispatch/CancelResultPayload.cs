using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Dispatch;

public sealed record CancelResultPayload(bool Cancelled, long? CancelledOrderId, string OriginNodeId, EventOccurrence Cause) : EventPayload
{
    public override EventPayloadType PayloadType => EventPayloadType.CancelResult;
}