namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record GraphLayoutResponse(IReadOnlyDictionary<string, NodePositionResponse> Positions);