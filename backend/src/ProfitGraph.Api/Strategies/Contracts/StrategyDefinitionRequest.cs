namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyDefinitionRequest(
    IReadOnlyList<StrategyParameterRequest> Parameters,
    IReadOnlyList<StrategyValueRequest> Values,
    IReadOnlyList<StrategyBlockRequest> Blocks,
    IReadOnlyList<StrategyWireRequest> Wires,
    string? TemplateKind = null);