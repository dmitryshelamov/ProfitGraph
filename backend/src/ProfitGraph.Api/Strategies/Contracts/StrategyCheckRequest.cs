using System.Text.Json.Serialization;

namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyCheckRequest(
    string Type,
    string? Operation = null,
    IReadOnlyDictionary<string, ValueExpressionRequest>? Operands = null,
    ValueExpressionRequest? Tag = null,
    string? Side = null,
    string? OrderKind = null,
    string? SizeMode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MinimumOrderPolicy = null);