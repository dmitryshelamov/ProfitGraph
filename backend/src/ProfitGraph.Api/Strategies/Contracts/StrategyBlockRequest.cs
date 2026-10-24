using System.Text.Json.Serialization;

namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyBlockRequest(
    string Id,
    string Kind,
    string? DisplayName = null,
    string? Source = null,
    string? Join = null,
    IReadOnlyList<StrategyCheckRequest>? Checks = null,
    string? OrderKind = null,
    string? Side = null,
    ValueExpressionRequest? Tag = null,
    ValueExpressionRequest? Amount = null,
    ValueExpressionRequest? Quantity = null,
    ValueExpressionRequest? Price = null,
    string? SizeMode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MinimumOrderPolicy = null);