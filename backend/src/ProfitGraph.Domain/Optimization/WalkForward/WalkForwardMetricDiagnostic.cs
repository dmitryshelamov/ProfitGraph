namespace ProfitGraph.Domain.Optimization.WalkForward;

public sealed record WalkForwardMetricDiagnostic(string Code, string Metric, int? WindowIndex = null);