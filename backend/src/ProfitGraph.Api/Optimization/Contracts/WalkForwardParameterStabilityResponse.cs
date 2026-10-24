namespace ProfitGraph.Api.Optimization.Contracts;

/// <param name="RangeInSteps">How far apart the window winners are, in grid steps.</param>
public sealed record WalkForwardParameterStabilityResponse(
    string Parameter,
    IReadOnlyList<WalkForwardParameterWinnerResponse> Winners,
    int DistinctCount,
    string? RangeInSteps);