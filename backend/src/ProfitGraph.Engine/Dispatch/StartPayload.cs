using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Dispatch;

public sealed record StartPayload : EventPayload
{
    public override EventPayloadType PayloadType => EventPayloadType.Start;
}