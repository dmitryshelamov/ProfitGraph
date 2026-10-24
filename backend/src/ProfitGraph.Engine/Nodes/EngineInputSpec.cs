using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     A value input that may resolve from a connection, an inline parameter or a typed default.
/// </summary>
public sealed record EngineInputSpec
{
    public string Key { get; }
    public ValueType ValueType { get; }
    public bool Required { get; }
    public bool SupportsInline { get; }
    public EngineValue? DefaultValue { get; }

    public EngineInputSpec(string key, ValueType valueType, bool required, bool supportsInline, EngineValue? defaultValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!Enum.IsDefined(valueType) || valueType == ValueType.Event
            || (supportsInline && valueType is ValueType.Candle or ValueType.Order or ValueType.Fill)
            || (defaultValue is not null && (valueType is ValueType.Candle or ValueType.Order or ValueType.Fill
                || !defaultValue.Value.IsAvailable || defaultValue.Value.ValueType != valueType)))
        {
            throw new ArgumentException("Input metadata must describe a matching available typed value.");
        }

        Key = key;
        ValueType = valueType;
        Required = required;
        SupportsInline = supportsInline;
        DefaultValue = defaultValue;
    }
}