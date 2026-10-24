namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record BlockCatalogOperandInternal(string Key, string Label, IReadOnlyList<string> ValueTypes, bool Required);