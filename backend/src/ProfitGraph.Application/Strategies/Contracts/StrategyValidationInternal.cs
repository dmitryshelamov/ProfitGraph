namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record StrategyValidationInternal(bool IsValid, int? Version, IReadOnlyList<StrategyDiagnosticInternal> Diagnostics);