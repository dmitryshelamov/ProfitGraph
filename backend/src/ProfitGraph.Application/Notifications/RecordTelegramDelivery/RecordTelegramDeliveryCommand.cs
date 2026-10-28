using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Notifications.RecordTelegramDelivery;

/// <summary>
///     Records how Telegram answered a claimed notification (ADR 0042); an outcome of a sender whose claim is no longer the
///     current one changes nothing. A blocked chat is also marked blocked, so it receives nothing more.
/// </summary>
public sealed record RecordTelegramDeliveryCommand(
    Guid NotificationId,
    Guid ClaimId,
    long ChatId,
    TelegramSendOutcome Outcome,
    TimeSpan? RetryAfter,
    string? Error) : ICommand;