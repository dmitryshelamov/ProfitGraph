using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The bot's fee rates changed after a read from the venue.
/// </summary>
public sealed record BotFeeRatesChanged(BotId BotId, DateTimeOffset At) : IDomainEvent;