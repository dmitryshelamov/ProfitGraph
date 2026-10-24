using ProfitGraph.Engine.Graphs.Validation;

namespace ProfitGraph.Engine.Blocks;

public sealed record StrategyBlockDiagnostic(
    GraphDiagnosticCode Code,
    string Message,
    string? BlockId = null,
    string? ValueName = null,
    string? ParameterName = null,
    int? CheckIndex = null);