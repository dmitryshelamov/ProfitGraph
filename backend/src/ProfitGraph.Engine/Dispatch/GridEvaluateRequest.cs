using ProfitGraph.Engine.Grids;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     An occurrence a grid reacts to: its own order's outcome, its start, a candle close or a resume. Settings are null when a
///     value is unavailable or out of range, and then the grid only books and stops.
/// </summary>
public sealed record GridEvaluateRequest(string NodeId, GridSettings? Settings) : ActionRequest(NodeId);