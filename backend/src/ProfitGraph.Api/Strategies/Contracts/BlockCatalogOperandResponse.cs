namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record BlockCatalogOperandResponse(string Key, string Label, IReadOnlyList<string> ValueTypes, bool Required);