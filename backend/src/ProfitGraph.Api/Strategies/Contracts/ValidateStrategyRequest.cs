using System.ComponentModel.DataAnnotations;

namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record ValidateStrategyRequest([Required] StrategyDefinitionRequest Definition);