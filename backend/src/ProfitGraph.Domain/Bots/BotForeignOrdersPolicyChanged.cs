using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     The owner allowed or disallowed foreign orders on the bot's symbol.
/// </summary>
public sealed record BotForeignOrdersPolicyChanged(BotId BotId, bool AllowForeignOrders, DateTimeOffset At) : IDomainEvent;