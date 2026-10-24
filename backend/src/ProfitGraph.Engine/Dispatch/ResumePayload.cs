using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Dispatch;

public sealed record ResumePayload(ResumeReason Reason, string PreviousGraphVersionId, string CurrentGraphVersionId) : EventPayload
{
    public override EventPayloadType PayloadType => EventPayloadType.Resume;
}