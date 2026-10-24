using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProfitGraph.Infrastructure.Common.Outbox;

/// <summary>
///     Writes a single-value Domain value object, such as <c>BotId</c> or <c>VenueId</c>, as its <c>Value</c> and reads it
///     back through the constructor that validates it.
/// </summary>
internal sealed class ValueObjectJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return Describe(typeToConvert) is not null;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        (PropertyInfo value, ConstructorInfo constructor) =
            Describe(typeToConvert) ?? throw new InvalidOperationException($"{typeToConvert.Name} is not a value object.");
        Type converter = typeof(ValueObjectJsonConverter<,>).MakeGenericType(typeToConvert, value.PropertyType);

        return (JsonConverter)Activator.CreateInstance(converter, value, constructor)!;
    }

    private static (PropertyInfo Value, ConstructorInfo Constructor)? Describe(Type type)
    {
        if (type.IsPrimitive || type == typeof(string) || type.Namespace?.StartsWith("ProfitGraph.Domain", StringComparison.Ordinal) != true)
        {
            return null;
        }

        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo? value = properties is [{ Name: "Value" } single] ? single : null;
        ConstructorInfo? constructor = value is null
            ? null
            : type.GetConstructors().FirstOrDefault(candidate => candidate.GetParameters() is [{ } parameter] && parameter.ParameterType == value.PropertyType);

        return value is not null && constructor is not null ? (value, constructor) : null;
    }
}