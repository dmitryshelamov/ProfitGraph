namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Inspectable suppression reason; no rejection or trading event is fabricated for an unavailable action.
/// </summary>
public sealed record ActionSkipDiagnostic(string NodeId, long OccurrenceId, string Reason);