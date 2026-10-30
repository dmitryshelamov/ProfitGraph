using System.Text.Json;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Scanner.Repositories.Converters;

internal static class ScanPresetDbConverter
{
    /// <summary>
    ///     Storage format of a preset definition; a change ships an explicit upgrade of stored presets.
    /// </summary>
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public static string ToDefinitionJson(this ScanPreset preset)
    {
        var definition = new ScanPresetDefinitionDb(
            FormatVersion,
            [.. preset.Conditions.Select(condition => new ScanPresetConditionDb(condition.Metric.ToString(), condition.Minimum, condition.Maximum))],
            preset.Sort.Metric.ToString(),
            preset.Sort.Descending,
            preset.HideStale,
            preset.TradeDirection.ToString());

        return JsonSerializer.Serialize(definition, Options);
    }

    public static ScanPreset ToDomain(this ScanPresetDb row)
    {
        ScanPresetDefinitionDb definition = JsonSerializer.Deserialize<ScanPresetDefinitionDb>(row.DefinitionJson, Options) ??
            throw new InvalidOperationException("A stored scanner preset has no definition.");

        if (definition.FormatVersion != FormatVersion)
        {
            throw new InvalidOperationException($"A stored scanner preset has format {definition.FormatVersion}, not {FormatVersion}.");
        }

        return ScanPreset.Restore(
            new ScanPresetId(row.Id),
            new UserId(row.OwnerId),
            new VenueId(row.Venue),
            new ScanPresetName(row.Name),
            [.. definition.Conditions.Select(condition => new ScanCondition(Enum.Parse<ScanMetric>(condition.Metric), condition.Minimum, condition.Maximum))],
            new ScanSort(Enum.Parse<ScanMetric>(definition.SortMetric), definition.SortDescending),
            definition.HideStale,
            row.CreatedAt,
            row.UpdatedAt,
            definition.TradeDirection is { } direction ? Enum.Parse<ScanTradeDirection>(direction) : ScanTradeDirection.Both);
    }
}