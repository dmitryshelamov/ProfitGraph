namespace ProfitGraph.Application.Optimization.Contracts;

/// <summary>
///     What the optimization aggregate does not hold: the job's status and the finalist runs it started, oldest first.
/// </summary>
public sealed record OptimizationStateInternal(string Status, IReadOnlyList<Guid> Finalists);