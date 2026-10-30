namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     The mode to switch a preset's auto trade rule to: <c>Off</c> or <c>Paper</c> (ADR 0071, sections 5 and 7).
/// </summary>
public sealed record ScanAutoTradeModeRequest(string Mode);