namespace ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;

/// <summary>
///     An <c>optimization_windows</c> row; parameters, metrics and equity are JSON.
/// </summary>
internal sealed class OptimizationWindowDb
{
    public int WindowIndex { get; init; }
    public int TrainStart { get; init; }
    public int TrainCount { get; init; }
    public int TestStart { get; init; }
    public int TestCount { get; init; }
    public DateTimeOffset TrainFrom { get; init; }
    public DateTimeOffset TrainTo { get; init; }
    public DateTimeOffset TestFrom { get; init; }
    public DateTimeOffset TestTo { get; init; }
    public int? WinnerTrialIndex { get; init; }
    public string? Parameters { get; init; }
    public string? TrainMetrics { get; init; }
    public string? TestMetrics { get; init; }
    public string Status { get; init; } = null!;
    public string? FailureCode { get; init; }
    public string Equity { get; init; } = null!;
}