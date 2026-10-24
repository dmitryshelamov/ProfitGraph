using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfitGraph.Infrastructure.Backtesting;

/// <summary>
///     The JSON form of stored run data: camel-case names, enums by name and decimals as exact JSON numbers, which
///     <c>jsonb</c> keeps as exact <c>numeric</c>.
/// </summary>
internal static class BacktestJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    public static JsonElement ToElement<T>(T value)
    {
        return JsonSerializer.SerializeToElement(value, Options);
    }

    public static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidOperationException($"Stored {typeof(T).Name} is empty.");
    }
}