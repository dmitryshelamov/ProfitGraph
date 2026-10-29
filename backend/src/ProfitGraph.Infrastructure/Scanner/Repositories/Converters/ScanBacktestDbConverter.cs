using System.Text.Json;

using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Scanner.Repositories.Converters;

/// <summary>
///     The filter is stored in its own versioned format; the result is the engine's records as JSON, decimals as exact numbers.
/// </summary>
internal static class ScanBacktestDbConverter
{
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public static string ToFilterJson(this ScanBacktest backtest)
    {
        return JsonSerializer.Serialize(
            new ScanFilterDb(
                FormatVersion,
                [.. backtest.Filter.Conditions.Select(condition => new ScanPresetConditionDb(condition.Metric.ToString(), condition.Minimum, condition.Maximum))],
                backtest.Filter.HideStale),
            Options);
    }

    public static string? ToResultJson(this ScanBacktest backtest)
    {
        return backtest.Result is null ? null : JsonSerializer.Serialize(backtest.Result, Options);
    }

    public static ScanBacktest ToDomain(this ScanBacktestDb row)
    {
        ScanFilterDb filter = JsonSerializer.Deserialize<ScanFilterDb>(row.FilterJson, Options) ??
            throw new InvalidOperationException("A stored scanner backtest has no filter.");

        if (filter.FormatVersion != FormatVersion)
        {
            throw new InvalidOperationException($"A stored scanner backtest has format {filter.FormatVersion}, not {FormatVersion}.");
        }

        return ScanBacktest.Restore(
            new ScanBacktestId(row.Id),
            new UserId(row.OwnerId),
            new VenueId(row.Venue),
            new ScanPresetName(row.PresetName),
            new ScanFilter(
                [.. filter.Conditions.Select(condition => new ScanCondition(Enum.Parse<ScanMetric>(condition.Metric), condition.Minimum, condition.Maximum))],
                filter.HideStale),
            row.PeriodFrom,
            row.PeriodTo,
            new BackgroundJobId(row.JobId),
            row.CreatedAt,
            row.CompletedAt,
            row.ResultJson is null ? null : JsonSerializer.Deserialize<ScanBacktestResult>(row.ResultJson, Options));
    }

    public static ScanBacktestListItemInternal ToInternal(this ScanBacktestListItemDb row)
    {
        return new ScanBacktestListItemInternal(
            row.Id,
            row.PresetName,
            row.PeriodFrom,
            row.PeriodTo,
            row.JobId,
            row.JobStatus,
            row.CreatedAt,
            row.CompletedAt,
            row.Signals);
    }
}