using System.ComponentModel.DataAnnotations;

namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record SaveStrategyRequest(
    [Range(minimum: 1, int.MaxValue)] int ExpectedVersion,
    [Required] string Name,
    [Required] StrategyDefinitionRequest Definition,
    [Required] GraphLayoutRequest Layout,
    bool ConfirmLiveImpact = false);