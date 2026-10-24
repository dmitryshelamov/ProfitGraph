namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record StrategyDiagnosticResponse(
    string Code,
    string Message,
    string? BlockId,
    string? ValueName,
    string? ParameterName,
    string? Setting,
    int? CheckIndex = null);