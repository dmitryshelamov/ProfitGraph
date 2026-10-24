using System.Collections.Immutable;

namespace ProfitGraph.Engine.Dispatch;

public sealed record GraphChangeResult(
    bool Applied,
    string? Diagnostic,
    ImmutableArray<long> CancelledOrderIds,
    ImmutableArray<string> KeptIndicatorNodeIds,
    bool AreIndicatorsReady,
    ImmutableArray<EventOccurrence> Occurrences,
    ImmutableArray<DispatchObservation> Observations);