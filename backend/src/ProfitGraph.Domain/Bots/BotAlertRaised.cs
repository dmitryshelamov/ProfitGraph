using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     An alert was stored for the bot's owner (ADR 0042), so it can reach the owner's Telegram chat.
/// </summary>
public sealed record BotAlertRaised(BotAlertId AlertId, BotId BotId, UserId OwnerId, BotAlertKind Kind, string? Reason, DateTimeOffset At) : IDomainEvent;