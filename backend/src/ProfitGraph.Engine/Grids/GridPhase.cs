namespace ProfitGraph.Engine.Grids;

/// <summary>
///     Whether a grid runs: <see cref="Stopped" /> before its first start and after a stop, <see cref="Stopping" /> while it
///     cancels its orders and, for the stop below the grid, sells what its levels bought.
/// </summary>
public enum GridPhase
{
    Stopped,
    Running,
    Stopping
}