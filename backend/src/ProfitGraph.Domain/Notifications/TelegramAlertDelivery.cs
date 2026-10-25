using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Domain.Notifications;

/// <summary>
///     An alert waiting to reach its owner's linked chat, with what the message needs.
/// </summary>
public sealed record TelegramAlertDelivery(BotAlertId AlertId, long ChatId, BotAlertKind Kind, string? Reason, string Symbol, string Timeframe);