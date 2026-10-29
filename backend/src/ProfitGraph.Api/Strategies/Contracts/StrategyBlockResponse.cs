using System.Text.Json.Serialization;

namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyBlockResponse(
    string Id,
    string Kind,
    string? DisplayName = null,
    string? Source = null,
    string? Join = null,
    IReadOnlyList<StrategyCheckResponse>? Checks = null,
    string? OrderKind = null,
    string? Side = null,
    ValueExpressionResponse? Tag = null,
    ValueExpressionResponse? Amount = null,
    ValueExpressionResponse? Quantity = null,
    ValueExpressionResponse? Price = null,
    string? SizeMode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MinimumOrderPolicy = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, ValueExpressionResponse>? Settings = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Grid = null);