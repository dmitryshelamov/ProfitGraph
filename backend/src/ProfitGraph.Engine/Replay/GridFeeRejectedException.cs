using ProfitGraph.Engine.Common;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Ends a replay before its first candle when the grid fee check found an error; the run's diagnostic explains it.
/// </summary>
internal sealed class GridFeeRejectedException : EngineException
{
    public GridFeeRejectedException() : base("A grid's profit step does not cover its fees.")
    {
    }
}