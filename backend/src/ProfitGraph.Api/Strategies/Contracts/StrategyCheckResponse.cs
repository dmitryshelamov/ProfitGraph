using System.Text.Json.Serialization;

namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyCheckResponse(
    string Type,
    string? Operation = null,
    IReadOnlyDictionary<string, ValueExpressionResponse>? Operands = null,
    ValueExpressionResponse? Tag = null,
    string? Side = null,
    string? OrderKind = null,
    string? SizeMode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MinimumOrderPolicy = null);