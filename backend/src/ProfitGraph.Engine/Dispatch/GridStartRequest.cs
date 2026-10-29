using ProfitGraph.Engine.Grids;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     An event reached a grid block: it starts the grid unless the grid runs.
/// </summary>
public sealed record GridStartRequest(string NodeId, GridSettings Settings) : ActionRequest(NodeId);