using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.Contracts;

/// <param name="Ranking">Every delivered trial, template rejections included, in ranking order.</param>
/// <param name="CompletedTrials">Executed trials, whatever their status; template rejections are not executed.</param>
/// <param name="TotalTrials">Trials the search planned to execute.</param>
public sealed record OptimizationRunResultInternal(
    IReadOnlyList<OptimizationTrialResult> Ranking,
    int CompletedTrials,
    int TotalTrials,
    int RejectedByTemplate,
    bool IsCancelled);