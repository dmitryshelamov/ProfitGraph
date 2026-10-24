namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record BlockCatalogEntryInternal(
    string Key,
    string Label,
    IReadOnlyList<BlockCatalogOperandInternal> Operands,
    string? OutputType = null,
    IReadOnlyList<string>? Choices = null,
    IReadOnlyList<BlockCatalogOperandInternal>? Fields = null,
    IReadOnlyList<string>? Sides = null,
    IReadOnlyList<string>? SizeModes = null,
    IReadOnlyList<string>? OrderKinds = null,
    IReadOnlyList<string>? MinimumOrderPolicies = null);