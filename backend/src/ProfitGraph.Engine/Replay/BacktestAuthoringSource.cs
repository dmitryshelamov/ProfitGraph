namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Immutable authoring provenance for a frozen execution node, independent of later strategy versions.
/// </summary>
public sealed record BacktestAuthoringSource(string? BlockId, string? ValueName, string? ParameterName, string? DisplayName = null, int? CheckIndex = null);