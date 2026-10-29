using System.Text.Json;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Scanner.Repositories.Converters;

internal static class ScanAlertWatchDbConverter
{
    public static ScanAlertWatch ToDomain(this ScanAlertWatchDb row)
    {
        Dictionary<string, ScanAlertPairDb> pairs = JsonSerializer.Deserialize<Dictionary<string, ScanAlertPairDb>>(row.Pairs) ?? [];

        return ScanAlertWatch.Restore(
            new ScanPresetId(row.PresetId),
            new UserId(row.OwnerId),
            row.EnabledAt,
            row.CheckedAt,
            pairs.ToDictionary(pair => pair.Key, pair => new ScanAlertPair(pair.Value.InResult, pair.Value.AlertedAt), StringComparer.Ordinal));
    }

    public static string ToPairsJson(this ScanAlertWatch watch)
    {
        return JsonSerializer.Serialize(
            watch.Pairs.ToDictionary(pair => pair.Key, pair => new ScanAlertPairDb { InResult = pair.Value.InResult, AlertedAt = pair.Value.AlertedAt }));
    }
}