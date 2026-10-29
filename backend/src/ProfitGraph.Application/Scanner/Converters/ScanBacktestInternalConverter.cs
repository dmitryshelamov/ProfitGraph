using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.Converters;

internal static class ScanBacktestInternalConverter
{
    public static ScanBacktestInternal ToInternal(this ScanBacktest backtest)
    {
        return new ScanBacktestInternal(
            backtest.Id.Value,
            backtest.Venue.Value,
            backtest.PresetName.Value,
            [.. backtest.Filter.Conditions.Select(condition => new ScanConditionInternal(condition.Metric.ToString(), condition.Minimum, condition.Maximum))],
            backtest.Filter.HideStale,
            backtest.From,
            backtest.To,
            backtest.JobId.Value,
            backtest.CreatedAt,
            backtest.CompletedAt,
            backtest.Result?.Pairs,
            backtest.Result?.FailedPairs,
            backtest.Result?.Summary,
            backtest.Result?.Signals,
            backtest.Result?.SignalsTruncated ?? false);
    }
}