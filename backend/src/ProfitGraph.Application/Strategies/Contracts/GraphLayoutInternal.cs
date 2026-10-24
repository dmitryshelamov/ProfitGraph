namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record GraphLayoutInternal(IReadOnlyDictionary<string, NodePositionInternal> Positions);