using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Engine.Graphs;

/// <summary>
///     A closed concrete rejection reason used by ordinary graph equality expressions.
/// </summary>
public sealed record RejectionReasonParamLiteral(RejectionReason Value) : ParamLiteral;