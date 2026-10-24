using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Dispatch;

public sealed record CandlePayload(Candle Candle) : EventPayload
{
    public override EventPayloadType PayloadType => EventPayloadType.Candle;
}