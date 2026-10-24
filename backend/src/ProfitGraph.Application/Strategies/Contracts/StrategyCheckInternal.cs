namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record StrategyCheckInternal(
    string Type,
    string? Operation = null,
    IReadOnlyDictionary<string, ValueExpressionInternal>? Operands = null,
    ValueExpressionInternal? Tag = null,
    string? Side = null,
    string? OrderKind = null,
    string? SizeMode = null,
    string? MinimumOrderPolicy = null);