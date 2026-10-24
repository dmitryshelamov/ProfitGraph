using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     One unit of work on a bot's queue: a live closed candle or a venue update.
/// </summary>
internal sealed record BotRoot(Candle? Candle, ExternalOrderUpdate? Update);