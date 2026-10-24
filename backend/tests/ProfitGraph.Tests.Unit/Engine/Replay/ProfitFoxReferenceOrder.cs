using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

internal sealed record ProfitFoxReferenceOrder(
    OrderSide Side,
    OrderType Type,
    decimal? Quantity,
    decimal? Price,
    decimal? QuoteBudget,
    string Tag,
    int CandleIndex,
    PricePhase Phase,
    decimal MarketPrice);