using ProfitGraph.Domain.Optimization;
using ProfitGraph.Domain.Optimization.Search;

namespace ProfitGraph.Application.Optimization.Contracts;

/// <param name="Ranking">Every delivered trial, template rejections included, in ranking order.</param>
/// <param name="CompletedTrials">Executed trials, whatever their status; template rejections are not executed.</param>
/// <param name="TotalTrials">Trials the search planned to execute.</param>
/// <param name="NeighborhoodScores">By trial index: how its evaluated grid neighbors scored; empty when cancelled.</param>
public sealed record OptimizationRunResultInternal(
    IReadOnlyList<OptimizationTrialResult> Ranking,
    int CompletedTrials,
    int TotalTrials,
    int RejectedByTemplate,
    bool IsCancelled,
    IReadOnlyDictionary<int, OptimizationNeighborhoodScore> NeighborhoodScores);