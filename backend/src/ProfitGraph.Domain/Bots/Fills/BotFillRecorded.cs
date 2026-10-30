using ProfitGraph.Domain.Common;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Domain.Bots.Fills;

public sealed record BotFillRecorded(
    BotId BotId,
    BotFillId FillId,
    long EngineOrderId,
    OrderSide Side,
    decimal GrossBase,
    decimal GrossQuote,
    DateTimeOffset At,
    decimal Fee,
    FeeAsset FeeAsset) : IDomainEvent;