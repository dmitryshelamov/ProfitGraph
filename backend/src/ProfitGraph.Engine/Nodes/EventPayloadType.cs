namespace ProfitGraph.Engine.Nodes;

public enum EventPayloadType
{
    Start,
    Candle,
    Fill,
    Order,
    Rejection,
    CancelResult,
    Resume
}