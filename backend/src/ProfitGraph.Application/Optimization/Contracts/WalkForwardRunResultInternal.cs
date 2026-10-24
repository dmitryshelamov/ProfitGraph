using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.Contracts;

/// <param name="Windows">The finished windows in order; a cancelled run ends after the last finished one.</param>
public sealed record WalkForwardRunResultInternal(
    IReadOnlyList<WalkForwardWindowResult> Windows,
    WalkForwardSummary Summary,
    int CompletedTrials,
    int TotalTrials,
    int RejectedByTemplate,
    bool IsCancelled);