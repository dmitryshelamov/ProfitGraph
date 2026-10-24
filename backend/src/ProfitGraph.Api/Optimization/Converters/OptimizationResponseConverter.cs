using System.Globalization;

using ProfitGraph.Api.Backtesting.Converters;
using ProfitGraph.Api.Optimization.Contracts;
using ProfitGraph.Application.Common.Numbers;
using ProfitGraph.Application.Optimization.Contracts;

namespace ProfitGraph.Api.Optimization.Converters;

public static class OptimizationResponseConverter
{
    public static OptimizationRunResponse ToResponse(this OptimizationRunInternal optimization)
    {
        OptimizationConfigurationInternal configuration = optimization.Configuration;
        OptimizationCountersInternal counters = optimization.Counters;

        return new OptimizationRunResponse(
            optimization.Id,
            optimization.StrategyId,
            optimization.StrategyVersion,
            optimization.State.Status,
            optimization.CreatedAt,
            new OptimizationConfigurationResponse(
                configuration.Backtest.ToResponse(),
                configuration.Ranges.ToDictionary(
                    pair => pair.Key,
                    pair => new OptimizationRangeResponse(Text(pair.Value.Min), Text(pair.Value.Max), Text(pair.Value.Step)),
                    StringComparer.Ordinal),
                configuration.Method,
                configuration.Objective,
                configuration.Budget,
                configuration.Seed.ToString(CultureInfo.InvariantCulture),
                configuration.FinalistCount,
                configuration.MinExecutions,
                Text(configuration.MaxDrawdownPercent)),
            optimization.Stage,
            optimization.History?.ToResponse(),
            optimization.PlannedTrials,
            new OptimizationCountersResponse(counters.Completed, counters.RejectedByConstraint, counters.RejectedByTemplate, counters.Failed),
            optimization.Best is { } best ? new OptimizationBestTrialResponse(best.TrialIndex, Text(best.Objective)) : null,
            optimization.State.Finalists);
    }

    public static OptimizationTrialPageResponse ToResponse(this OptimizationTrialPageInternal page)
    {
        return new OptimizationTrialPageResponse([.. page.Items.Select(trial => trial.ToResponse())], page.Next);
    }

    public static OptimizationTrialResponse ToResponse(this OptimizationTrialInternal trial)
    {
        return new OptimizationTrialResponse(
            trial.Sequence,
            trial.TrialIndex,
            trial.Parameters.ToDictionary(pair => pair.Key, pair => Text(pair.Value), StringComparer.Ordinal),
            trial.Status,
            trial.FailureCode,
            Text(trial.Objective),
            Text(trial.ReturnPercent),
            Text(trial.TotalPnl),
            Text(trial.MaxDrawdownPercent),
            trial.Executions,
            trial.ElapsedMs,
            trial.Stage,
            trial.Neighborhood is { } neighborhood
                ? new OptimizationNeighborhoodResponse(neighborhood.Evaluated, neighborhood.Eligible, Text(neighborhood.Mean))
                : null);
    }

    public static OptimizationSummaryPageResponse ToResponse(this OptimizationSummaryPageInternal page)
    {
        return new OptimizationSummaryPageResponse(
            [
                .. page.Items.Select(summary => new OptimizationSummaryResponse(
                    summary.Id,
                    summary.StrategyVersion,
                    summary.CreatedAt,
                    summary.Venue,
                    summary.Symbol,
                    summary.Timeframe,
                    summary.Method,
                    summary.Status,
                    summary.ExecutedTrials,
                    Text(summary.BestObjective)))
            ],
            page.Next);
    }

    private static string Text(decimal value)
    {
        return ExactDecimalText.Format(value);
    }

    private static string? Text(decimal? value)
    {
        return value is { } number ? ExactDecimalText.Format(number) : null;
    }
}