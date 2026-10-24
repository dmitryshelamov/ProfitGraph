using System.ComponentModel.DataAnnotations;

namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record GraphLayoutRequest([Required] IReadOnlyDictionary<string, NodePositionRequest> Positions);