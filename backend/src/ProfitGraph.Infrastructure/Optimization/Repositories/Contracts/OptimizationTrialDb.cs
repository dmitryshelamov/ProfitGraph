namespace ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

/// <summary>
///     An <c>optimization_trials</c> row; parameters are a JSON object of exact decimals by name.
/// </summary>
internal sealed class OptimizationTrialDb
{
    public Guid OptimizationId { get; init; }
    public long Sequence { get; init; }
    public int TrialIndex { get; init; }
    public string Parameters { get; init; } = null!;
    public string Status { get; init; } = null!;
    public string? FailureCode { get; init; }
    public decimal? Objective { get; init; }
    public decimal? ReturnPercent { get; init; }
    public decimal? TotalPnl { get; init; }
    public decimal? MaxDrawdownPercent { get; init; }
    public long Executions { get; init; }
    public long ElapsedMs { get; init; }
    public int Stage { get; init; }
    public int WindowIndex { get; init; }
    public int? NeighborsEvaluated { get; init; }
    public int? NeighborsEligible { get; init; }
    public decimal? NeighborhoodMean { get; init; }
}