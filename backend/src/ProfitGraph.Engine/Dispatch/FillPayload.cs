using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Dispatch;

public sealed record FillPayload(Fill Fill, string OriginNodeId) : EventPayload
{
    public override EventPayloadType PayloadType => EventPayloadType.Fill;
}