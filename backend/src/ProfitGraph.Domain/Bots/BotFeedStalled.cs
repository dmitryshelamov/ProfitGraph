using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The live candle feed of the bot's series stopped delivering closed candles.
/// </summary>
public sealed record BotFeedStalled(BotId BotId, DateTimeOffset At) : IDomainEvent;