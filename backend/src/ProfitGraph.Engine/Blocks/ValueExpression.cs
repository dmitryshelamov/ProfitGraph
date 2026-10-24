using System.Collections.ObjectModel;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Blocks;

public sealed record ValueExpression
{
    public string Kind { get; }
    public ValueType? ValueType { get; }
    public decimal? Number { get; }
    public string? Text { get; }
    public string? Name { get; }
    public string? Field { get; }
    public string? Operation { get; }
    public IReadOnlyDictionary<string, ValueExpression> Arguments { get; }

    public ValueExpression(
        string kind,
        ValueType? valueType = null,
        decimal? number = null,
        string? text = null,
        string? name = null,
        string? field = null,
        string? operation = null,
        IReadOnlyDictionary<string, ValueExpression>? arguments = null)
    {
        Kind = kind;
        ValueType = valueType;
        Number = number;
        Text = text;
        Name = name;
        Field = field;
        Operation = operation;
        Arguments = new ReadOnlyDictionary<string, ValueExpression>(
            new Dictionary<string, ValueExpression>(arguments ?? new Dictionary<string, ValueExpression>(), StringComparer.Ordinal));
    }
}