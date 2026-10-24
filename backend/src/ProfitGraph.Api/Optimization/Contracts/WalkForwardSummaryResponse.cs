namespace ProfitGraph.Api.Optimization.Contracts;

/// <param name="OverallWfe">Walk-forward efficiency: test return against train return, per bar, over all windows.</param>
public sealed record WalkForwardSummaryResponse(
    string? TestTotalPnl,
    string? TestCompoundedReturnPercent,
    int ProfitableWindows,
    int TestWindows,
    string? OverallWfe,
    IReadOnlyList<WalkForwardParameterStabilityResponse> ParameterStability,
    IReadOnlyList<WalkForwardDiagnosticResponse> Diagnostics);