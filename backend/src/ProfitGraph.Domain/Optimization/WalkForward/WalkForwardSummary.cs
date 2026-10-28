namespace ProfitGraph.Domain.Optimization.WalkForward;

public sealed record WalkForwardSummary(
    decimal? TestTotalPnl,
    decimal? TestCompoundedReturnPercent,
    int ProfitableWindows,
    int TestWindows,
    decimal? OverallWfe,
    IReadOnlyList<WalkForwardParameterStability> ParameterStability,
    IReadOnlyList<WalkForwardMetricDiagnostic> Diagnostics);