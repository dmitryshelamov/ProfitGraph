using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     A stopped bot still has submit intents; they wait until it starts again.
/// </summary>
public sealed record BotSubmitHeld(BotId BotId, DateTimeOffset At) : IDomainEvent;