using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Dispatch;

public sealed record RejectionPayload(long AttemptId, OrderRejection Rejection) : EventPayload
{
    public override EventPayloadType PayloadType => EventPayloadType.Rejection;
}