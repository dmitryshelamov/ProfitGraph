namespace ProfitGraph.Domain.Optimization.WalkForward;

public sealed record WalkForwardEfficiency(decimal? Value, IReadOnlyList<WalkForwardMetricDiagnostic> Diagnostics);