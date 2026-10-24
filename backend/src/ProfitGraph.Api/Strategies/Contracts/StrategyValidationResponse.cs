namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyValidationResponse(bool IsValid, int? Version, IReadOnlyList<StrategyDiagnosticResponse> Diagnostics);