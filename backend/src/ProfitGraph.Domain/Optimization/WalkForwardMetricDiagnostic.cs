namespace ProfitGraph.Domain.Optimization;

public sealed record WalkForwardMetricDiagnostic(string Code, string Metric, int? WindowIndex = null);