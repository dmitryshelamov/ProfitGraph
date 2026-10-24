namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record StrategyDiagnosticInternal(
    string Code,
    string Message,
    string? BlockId,
    string? ValueName,
    string? ParameterName,
    string? Setting = null,
    int? CheckIndex = null);