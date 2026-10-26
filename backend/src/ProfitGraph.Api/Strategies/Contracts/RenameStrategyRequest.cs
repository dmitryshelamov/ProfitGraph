using System.ComponentModel.DataAnnotations;

namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record RenameStrategyRequest([Required] string Name);