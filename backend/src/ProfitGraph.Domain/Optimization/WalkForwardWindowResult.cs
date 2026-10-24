using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Metrics;

namespace ProfitGraph.Domain.Optimization;

public sealed record WalkForwardWindowResult(
    WalkForwardWindow Window,
    DateTimeOffset TrainFrom,
    DateTimeOffset TrainTo,
    DateTimeOffset TestFrom,
    DateTimeOffset TestTo,
    int? WinnerTrialIndex,
    EngineExecutionInputs? Parameters,
    SampleMetrics? TrainMetrics,
    SampleMetrics? TestMetrics,
    WalkForwardWindowStatus Status,
    string? FailureCode,
    IReadOnlyList<WalkForwardEquityPoint> Equity)
{
    public decimal? Wfe => WalkForwardSummaryCalculator.WindowEfficiency(this).Value;
    public IReadOnlyList<WalkForwardMetricDiagnostic> Diagnostics => WalkForwardSummaryCalculator.WindowEfficiency(this).Diagnostics;
}