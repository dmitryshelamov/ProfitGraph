using System.Globalization;

using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Api.Backtesting.Converters;
using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.Optimization.Contracts;
using ProfitGraph.Application.Backtesting.StartBacktest;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.StartOptimization;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Api.Optimization.Converters;

public static class StartOptimizationRequestConverter
{
    public static StartOptimizationCommand ToCommand(this StartOptimizationRequest request, Guid userId)
    {
        var errors = new List<InputError>();
        StartBacktestCommand? backtest = null;

        try
        {
            backtest = (request.Backtest ?? new StartBacktestRequest(
                    Guid.Empty,
                    Version: null,
                    Venue: null,
                    Symbol: null,
                    Timeframe: null,
                    From: null,
                    To: null,
                    Parameters: null,
                    StartingCapital: null,
                    Slippage: null,
                    LimitFeeRate: null,
                    MarketFeeRate: null,
                    OutOfSampleFraction: null))
                .ToCommand(userId);
        }
        catch (ApplicationValidationException exception)
        {
            errors.AddRange(exception.Errors.Select(error => error with { Field = $"Backtest.{error.Field}" }));
        }

        Dictionary<string, OptimizationRangeInternal> ranges = (request.Ranges ?? new Dictionary<string, OptimizationRangeRequest>()).ToDictionary(
            pair => pair.Key,
            pair => new OptimizationRangeInternal(
                Number(pair.Value.Min, $"Ranges.{pair.Key}.Min", errors) ?? 0m,
                Number(pair.Value.Max, $"Ranges.{pair.Key}.Max", errors) ?? 0m,
                Number(pair.Value.Step, $"Ranges.{pair.Key}.Step", errors) ?? 0m),
            StringComparer.Ordinal);
        decimal? maxDrawdown = request.MaxDrawdownPercent is null ? null : Number(request.MaxDrawdownPercent, "MaxDrawdownPercent", errors);
        ulong? seed = null;

        if (request.Seed is not null)
        {
            if (ulong.TryParse(request.Seed, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed))
            {
                seed = parsed;
            }
            else
            {
                errors.Add(new InputError("invalid_number", "Seed", "Use an unsigned 64-bit integer as a decimal string."));
            }
        }

        if (errors.Count > 0)
        {
            throw new ApplicationValidationException(errors);
        }

        return new StartOptimizationCommand(
            backtest!,
            ranges,
            request.Method,
            request.Objective,
            request.Budget,
            seed,
            request.FinalistCount,
            request.MinExecutions,
            maxDrawdown,
            request.WalkForward is { } walkForward
                ? new WalkForwardSettingsInternal(
                    walkForward.Windows ?? WalkForwardSettings.DefaultWindows,
                    walkForward.TrainToTestRatio ?? WalkForwardSettings.DefaultTrainToTestRatio,
                    walkForward.Scheme ?? "rolling")
                : null);
    }

    private static decimal? Number(string? value, string field, List<InputError> errors)
    {
        if (value is not null && ExactGraphNumberParser.TryDecimal(value, out decimal number))
        {
            return number;
        }

        errors.Add(new InputError("invalid_number", field, "Use an exact invariant decimal string."));

        return null;
    }
}