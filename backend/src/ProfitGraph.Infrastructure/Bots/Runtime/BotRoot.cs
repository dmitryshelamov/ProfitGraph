using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     One unit of work on a bot's queue: a live closed candle, a venue update or an owner's manual order.
/// </summary>
internal sealed record BotRoot(Candle? Candle, ExternalOrderUpdate? Update, ManualOrderRequestId? ManualOrder = null);