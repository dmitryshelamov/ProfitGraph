using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Notifications.PruneTelegramNotifications;

/// <summary>
///     Deletes notifications that finished and were created more than <see cref="Domain.Notifications.TelegramNotification.Retention" />
///     ago (ADR 0042); returns how many.
/// </summary>
public sealed record PruneTelegramNotificationsCommand : ICommand<int>;