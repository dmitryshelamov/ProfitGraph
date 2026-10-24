namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Run-local drain buffers consumed synchronously before the next drain; public batches always copy these buffers.
/// </summary>
internal sealed class ReplayDispatchBatch(
    IReadOnlyList<AppliedActionFact> appliedActions,
    IReadOnlyList<EventOccurrence> occurrences)
{
    internal IReadOnlyList<AppliedActionFact> AppliedActions { get; } = appliedActions;
    internal IReadOnlyList<EventOccurrence> Occurrences { get; } = occurrences;
}