using System.Collections.ObjectModel;

namespace ProfitGraph.Engine.Blocks;

public sealed record StrategyCheck
{
    public string Type { get; }
    public string? Operation { get; }
    public IReadOnlyDictionary<string, ValueExpression> Operands { get; }
    public ValueExpression? Tag { get; }
    public string? Side { get; }
    public string? OrderKind { get; }
    public string? SizeMode { get; }
    public string? MinimumOrderPolicy { get; }

    public StrategyCheck(
        string type,
        string? operation = null,
        IReadOnlyDictionary<string, ValueExpression>? operands = null,
        ValueExpression? tag = null,
        string? side = null,
        string? orderKind = null,
        string? sizeMode = null,
        string? minimumOrderPolicy = null)
    {
        Type = type;
        Operation = operation;
        Operands = new ReadOnlyDictionary<string, ValueExpression>(
            new Dictionary<string, ValueExpression>(operands ?? new Dictionary<string, ValueExpression>(), StringComparer.Ordinal));
        Tag = tag;
        Side = side;
        OrderKind = orderKind;
        SizeMode = sizeMode;
        MinimumOrderPolicy = minimumOrderPolicy == "Reject" ? null : minimumOrderPolicy;
    }
}