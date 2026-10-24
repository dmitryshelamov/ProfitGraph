using ProfitGraph.Api.Backtesting.Contracts;

namespace ProfitGraph.Api.Optimization.Contracts;

/// <param name="Equity">The test range's close equity, at most 2000 points that each end a bucket of candles.</param>
public sealed record OptimizationWindowResponse(
    int Index,
    int TrainStart,
    int TrainCount,
    int TestStart,
    int TestCount,
    DateTimeOffset TrainFrom,
    DateTimeOffset TrainTo,
    DateTimeOffset TestFrom,
    DateTimeOffset TestTo,
    string Status,
    int? WinnerTrialIndex,
    IReadOnlyDictionary<string, string>? Parameters,
    SampleMetricsResponse? TrainMetrics,
    SampleMetricsResponse? TestMetrics,
    string? Wfe,
    string? FailureCode,
    IReadOnlyList<WalkForwardDiagnosticResponse> Diagnostics,
    IReadOnlyList<WalkForwardEquityPointResponse> Equity);