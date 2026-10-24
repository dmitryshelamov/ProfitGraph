namespace ProfitGraph.Domain.Optimization;

public sealed record WalkForwardEfficiency(decimal? Value, IReadOnlyList<WalkForwardMetricDiagnostic> Diagnostics);