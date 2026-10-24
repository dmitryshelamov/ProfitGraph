using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The live candle feed of the bot's series delivers closed candles again.
/// </summary>
public sealed record BotFeedRecovered(BotId BotId, DateTimeOffset At) : IDomainEvent;