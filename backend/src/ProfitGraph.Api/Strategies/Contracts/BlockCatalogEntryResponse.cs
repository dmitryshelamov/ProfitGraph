namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record BlockCatalogEntryResponse(
    string Key,
    string Label,
    IReadOnlyList<BlockCatalogOperandResponse> Operands,
    string? OutputType = null,
    IReadOnlyList<string>? Choices = null,
    IReadOnlyList<BlockCatalogOperandResponse>? Fields = null,
    IReadOnlyList<string>? Sides = null,
    IReadOnlyList<string>? SizeModes = null,
    IReadOnlyList<string>? OrderKinds = null,
    IReadOnlyList<string>? MinimumOrderPolicies = null);