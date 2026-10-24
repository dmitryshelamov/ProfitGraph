namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Copied observations and suppression reasons from a FIFO drain, including its accepted prefix on failure.
/// </summary>
public sealed class DispatchBatch
{
    public IReadOnlyList<DispatchObservation> Observations { get; }
    public IReadOnlyList<ActionSkipDiagnostic> Skips { get; }
    public IReadOnlyList<AppliedActionFact> AppliedActions { get; }
    internal IReadOnlyList<EventOccurrence> Occurrences { get; }

    public DispatchBatch(IEnumerable<DispatchObservation> observations, IEnumerable<ActionSkipDiagnostic> skips, IEnumerable<AppliedActionFact>? appliedActions = null)
        : this(observations, skips, appliedActions, occurrences: null)
    {
    }

    internal DispatchBatch(
        IEnumerable<DispatchObservation> observations,
        IEnumerable<ActionSkipDiagnostic> skips,
        IEnumerable<AppliedActionFact>? appliedActions,
        IEnumerable<EventOccurrence>? occurrences)
    {
        Observations = Array.AsReadOnly(observations.ToArray());
        Skips = Array.AsReadOnly(skips.ToArray());
        AppliedActions = Array.AsReadOnly(appliedActions?.ToArray() ?? Array.Empty<AppliedActionFact>());
        Occurrences = Array.AsReadOnly(occurrences?.ToArray() ?? Observations.Select(observation => observation.Occurrence).ToArray());
    }
}