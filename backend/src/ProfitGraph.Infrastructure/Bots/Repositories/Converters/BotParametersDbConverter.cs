using System.Text.Json;

using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

/// <summary>
///     Parameter values as a JSON object of decimal numbers, keyed by parameter name.
/// </summary>
internal static class BotParametersDbConverter
{
    public static BotParameters ToParameters(string json)
    {
        return new BotParameters(
            JsonSerializer.Deserialize<Dictionary<string, decimal>>(json) ?? throw new InvalidOperationException("Stored bot parameters are empty."));
    }

    public static string ToJson(BotParameters parameters)
    {
        return JsonSerializer.Serialize(parameters.Values);
    }
}