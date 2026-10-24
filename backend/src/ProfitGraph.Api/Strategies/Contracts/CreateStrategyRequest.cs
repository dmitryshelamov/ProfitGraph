using System.ComponentModel.DataAnnotations;

namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record CreateStrategyRequest(
    [Required] string Name,
    [Required] StrategyDefinitionRequest Definition,
    [Required] GraphLayoutRequest Layout);