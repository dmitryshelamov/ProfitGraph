using ProfitGraph.Api.Backtesting.Converters;
using ProfitGraph.Api.Optimization.Contracts;
using ProfitGraph.Application.Common.Numbers;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Api.Optimization.Converters;

public static class WalkForwardResponseConverter
{
    public static WalkForwardSummaryResponse ToResponse(this WalkForwardSummary summary)
    {
        return new WalkForwardSummaryResponse(
            Text(summary.TestTotalPnl),
            Text(summary.TestCompoundedReturnPercent),
            summary.ProfitableWindows,
            summary.TestWindows,
            Text(summary.OverallWfe),
            [
                .. summary.ParameterStability.Select(stability => new WalkForwardParameterStabilityResponse(
                    stability.Reference.NodeId,
                    [.. stability.Winners.Select(winner => new WalkForwardParameterWinnerResponse(winner.WindowIndex, ExactDecimalText.Format(winner.Value)))],
                    stability.DistinctCount,
                    Text(stability.RangeInSteps)))
            ],
            [.. summary.Diagnostics.Select(ToResponse)]);
    }

    public static OptimizationWindowResponse ToResponse(this WalkForwardWindowResult window)
    {
        return new OptimizationWindowResponse(
            window.Window.Index,
            window.Window.TrainStart,
            window.Window.TrainCount,
            window.Window.TestStart,
            window.Window.TestCount,
            window.TrainFrom,
            window.TrainTo,
            window.TestFrom,
            window.TestTo,
            window.Status switch
            {
                WalkForwardWindowStatus.Completed => "completed",
                WalkForwardWindowStatus.NoWinner => "no_winner",
                _ => "test_failed"
            },
            window.WinnerTrialIndex,
            window.Parameters?.OptimizedValues.ToDictionary(pair => pair.Key.NodeId, pair => ExactDecimalText.Format(pair.Value), StringComparer.Ordinal),
            window.TrainMetrics?.ToResponse(),
            window.TestMetrics?.ToResponse(),
            Text(window.Wfe),
            window.FailureCode,
            [.. window.Diagnostics.Select(ToResponse)],
            [.. window.Equity.Select(point => new WalkForwardEquityPointResponse(point.CandleIndex, point.Time, ExactDecimalText.Format(point.CloseEquity)))]);
    }

    private static WalkForwardDiagnosticResponse ToResponse(WalkForwardMetricDiagnostic diagnostic)
    {
        return new WalkForwardDiagnosticResponse(diagnostic.Code, diagnostic.Metric, diagnostic.WindowIndex);
    }

    private static string? Text(decimal? value)
    {
        return value is { } number ? ExactDecimalText.Format(number) : null;
    }
}