namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     The deterministic input clock of a typed engine node.
/// </summary>
public enum NodeClock
{
    Static,
    CandleClocked,
    Dispatch,
    Event,
    Action
}