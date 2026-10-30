using ProfitGraph.Domain.Bots.ManualOrders;
using ProfitGraph.Domain.Bots.OrderCancels;
using ProfitGraph.Domain.Bots.PositionCloses;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     One unit of work on a bot's queue: a live closed candle, a venue update, an owner's manual order, order cancel or position
///     close, or a newly saved strategy version to follow.
/// </summary>
internal sealed record BotRoot(
    Candle? Candle,
    ExternalOrderUpdate? Update,
    ManualOrderRequestId? ManualOrder = null,
    bool GraphChange = false,
    OrderCancelRequestId? OrderCancel = null,
    BotPositionCloseId? PositionClose = null);