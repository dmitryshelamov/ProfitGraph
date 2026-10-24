using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Dispatch;

public sealed record OrderPayload(OrderSnapshot Order) : EventPayload
{
    public override EventPayloadType PayloadType => EventPayloadType.Order;
}