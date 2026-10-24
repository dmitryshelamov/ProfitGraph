namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record StrategyBlockInternal(
    string Id,
    string Kind,
    string? DisplayName = null,
    string? Source = null,
    string? Join = null,
    IReadOnlyList<StrategyCheckInternal>? Checks = null,
    string? OrderKind = null,
    string? Side = null,
    ValueExpressionInternal? Tag = null,
    ValueExpressionInternal? Amount = null,
    ValueExpressionInternal? Quantity = null,
    ValueExpressionInternal? Price = null,
    string? SizeMode = null,
    string? MinimumOrderPolicy = null);