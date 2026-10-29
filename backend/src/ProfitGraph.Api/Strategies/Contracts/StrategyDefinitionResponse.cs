namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyDefinitionResponse(
    IReadOnlyList<StrategyParameterResponse> Parameters,
    IReadOnlyList<StrategyValueResponse> Values,
    IReadOnlyList<StrategyBlockResponse> Blocks,
    IReadOnlyList<StrategyWireResponse> Wires,
    string? TemplateKind = null,
    string? Direction = null);