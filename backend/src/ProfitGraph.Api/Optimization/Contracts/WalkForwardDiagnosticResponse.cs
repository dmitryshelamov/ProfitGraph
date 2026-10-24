namespace ProfitGraph.Api.Optimization.Contracts;

/// <summary>
///     Why a walk-forward metric is missing, for example a train return of zero that leaves efficiency undefined.
/// </summary>
public sealed record WalkForwardDiagnosticResponse(string Code, string Metric, int? WindowIndex);