using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfitGraph.Infrastructure.Strategies.Repositories.Converters;

/// <summary>
///     Reads only exact closed enum names, including rejection of numeric JSON and numeric strings.
/// </summary>
internal sealed class ClosedEnumJsonConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !Enum.TryParse(reader.GetString(), out T value) || !Enum.IsDefined(value) ||
            reader.GetString() != value.ToString())
        {
            throw new JsonException("Expected a known enum name.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
        {
            throw new JsonException("Unknown enum value.");
        }

        writer.WriteStringValue(value.ToString());
    }
}