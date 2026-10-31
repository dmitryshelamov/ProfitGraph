using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Api.Scanner.Converters;

internal static class ScanBacktestResponseConverter
{
    public static ScanBacktestResponse ToResponse(this ScanBacktestInternal backtest)
    {
        return new ScanBacktestResponse(
            backtest.Id,
            backtest.Venue,
            backtest.PresetName,
            [
                .. backtest.Conditions.Select(condition => new ScanConditionResponse(
                    condition.Metric,
                    condition.Minimum is { } minimum ? ExactGraphNumberParser.Format(minimum) : null,
                    condition.Maximum is { } maximum ? ExactGraphNumberParser.Format(maximum) : null))
            ],
            backtest.HideStale,
            backtest.From,
            backtest.To,
            backtest.JobId,
            backtest.CreatedAt,
            backtest.CompletedAt,
            backtest.Pairs,
            backtest.FailedPairs,
            backtest.Summary is { } summary ? ToResponse(summary) : null,
            backtest.Signals?.Select(ToResponse).ToArray(),
            backtest.SignalsTruncated);
    }

    public static ScanBacktestListItemResponse ToResponse(this ScanBacktestListItemInternal item)
    {
        return new ScanBacktestListItemResponse(item.Id, item.PresetName, item.From, item.To, item.JobId, item.JobStatus, item.CreatedAt, item.CompletedAt, item.Signals);
    }

    private static ScanStudySummaryResponse ToResponse(ScanStudySummary summary)
    {
        return new ScanStudySummaryResponse(
            summary.Signals,
            ToResponse(summary.Change15m),
            ToResponse(summary.Change1h),
            ToResponse(summary.Change4h),
            ToResponse(summary.Change24h),
            ToResponse(summary.MaxRise24h),
            ToResponse(summary.MaxDrop24h),
            [.. (summary.Scenarios ?? []).Select(ToResponse)],
            summary.Calibration is { } calibration ? ToResponse(calibration) : null);
    }

    private static ScanCardCalibrationResponse ToResponse(ScanCardCalibration calibration)
    {
        return new ScanCardCalibrationResponse(
            calibration.Sources,
            calibration.Cards,
            ExactGraphNumberParser.Format(calibration.Observed),
            ExactGraphNumberParser.Format(calibration.Predicted),
            ExactGraphNumberParser.Format(calibration.Brier),
            ExactGraphNumberParser.Format(calibration.GeometryBrier),
            ExactGraphNumberParser.Format(calibration.BandsBrier),
            [
                .. calibration.Bins.Select(bin => new ScanCalibrationBinResponse(
                    ExactGraphNumberParser.Format(bin.Lower),
                    ExactGraphNumberParser.Format(bin.Upper),
                    bin.Cards,
                    ExactGraphNumberParser.Format(bin.Predicted),
                    ExactGraphNumberParser.Format(bin.Observed)))
            ]);
    }

    private static ScanScenarioSummaryResponse ToResponse(ScanScenarioSummary scenario)
    {
        return new ScanScenarioSummaryResponse(
            scenario.Scenario.ToString(),
            scenario.Trades,
            ScanSnapshotResponseConverter.Text(scenario.WinningShare),
            ScanSnapshotResponseConverter.Text(scenario.MeanR),
            ScanSnapshotResponseConverter.Text(scenario.MedianR),
            ExactGraphNumberParser.Format(scenario.TotalR),
            ScanSnapshotResponseConverter.Text(scenario.MeanPercent),
            scenario.LongestLosingStreak,
            ScanSnapshotResponseConverter.Text(scenario.MeanMinutes),
            scenario.Direction.ToString());
    }

    private static ScanTradeResponse ToResponse(ScanTrade trade)
    {
        return new ScanTradeResponse(
            trade.Scenario.ToString(),
            ExactGraphNumberParser.Format(trade.Entry),
            ExactGraphNumberParser.Format(trade.Stop),
            ExactGraphNumberParser.Format(trade.Target),
            trade.EnteredAt,
            trade.ExitedAt,
            ExactGraphNumberParser.Format(trade.Exit),
            trade.Reason.ToString(),
            ExactGraphNumberParser.Format(trade.ResultR),
            ExactGraphNumberParser.Format(trade.ResultPercent),
            trade.Direction.ToString());
    }

    private static ScanOutcomeSummaryResponse ToResponse(ScanOutcomeSummary outcome)
    {
        return new ScanOutcomeSummaryResponse(
            outcome.Count,
            ScanSnapshotResponseConverter.Text(outcome.RisingShare),
            ScanSnapshotResponseConverter.Text(outcome.Mean),
            ScanSnapshotResponseConverter.Text(outcome.Median));
    }

    private static ScanSignalResponse ToResponse(ScanSignal signal)
    {
        return new ScanSignalResponse(
            signal.Symbol,
            signal.At,
            ScanSnapshotResponseConverter.Text(signal.EntryPrice),
            new ScanSnapshotRowInternal(signal.Symbol, signal.Metrics, Incomplete: false).ToResponse(),
            ScanSnapshotResponseConverter.Text(signal.Change15m),
            ScanSnapshotResponseConverter.Text(signal.Change1h),
            ScanSnapshotResponseConverter.Text(signal.Change4h),
            ScanSnapshotResponseConverter.Text(signal.Change24h),
            ScanSnapshotResponseConverter.Text(signal.MaxRise24h),
            ScanSnapshotResponseConverter.Text(signal.MaxDrop24h),
            [.. (signal.Trades ?? []).Select(ToResponse)]);
    }
}