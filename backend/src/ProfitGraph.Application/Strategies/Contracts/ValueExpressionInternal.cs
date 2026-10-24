using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record ValueExpressionInternal(
    string Kind,
    ValueType? ValueType = null,
    decimal? Number = null,
    string? Text = null,
    string? Name = null,
    string? Field = null,
    string? Operation = null,
    IReadOnlyDictionary<string, ValueExpressionInternal>? Arguments = null);