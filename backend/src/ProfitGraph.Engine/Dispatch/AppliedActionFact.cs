namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Immutable committed mutation results saved before guarded lifecycle publication, including failures after acceptance.
/// </summary>
public sealed class AppliedActionFact
{
    public string NodeId { get; }
    public long CauseId { get; }
    public long AttemptId { get; }
    public IReadOnlyList<LifecycleEmission> Results { get; }

    public AppliedActionFact(string nodeId, long causeId, long attemptId, IEnumerable<LifecycleEmission> results)
    {
        NodeId = nodeId;
        CauseId = causeId;
        AttemptId = attemptId;
        Results = Array.AsReadOnly(results.ToArray());
    }
}