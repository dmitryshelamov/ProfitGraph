namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record ValueExpressionResponse(
    string Kind,
    string? ValueType = null,
    string? Number = null,
    string? Text = null,
    string? Name = null,
    string? Field = null,
    string? Operation = null,
    IReadOnlyDictionary<string, ValueExpressionResponse>? Arguments = null);