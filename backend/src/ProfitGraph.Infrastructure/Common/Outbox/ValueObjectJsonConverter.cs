using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfitGraph.Infrastructure.Common.Outbox;

internal sealed class ValueObjectJsonConverter<TValueObject, TValue>(PropertyInfo value, ConstructorInfo constructor) : JsonConverter<TValueObject>
{
    public override TValueObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        TValue inner = JsonSerializer.Deserialize<TValue>(ref reader, options) ?? throw new JsonException($"{typeof(TValueObject).Name} needs a value.");

        return (TValueObject)constructor.Invoke([inner]);
    }

    public override void Write(Utf8JsonWriter writer, TValueObject valueObject, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, (TValue)value.GetValue(valueObject)!, options);
    }
}