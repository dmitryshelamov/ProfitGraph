using System.Text.Json;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Alerts;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Infrastructure.Notifications.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Notifications.Repositories.Converters;

internal static class TelegramNotificationDbConverter
{
    public static TelegramNotification ToDomain(this TelegramNotificationDb row)
    {
        return TelegramNotification.Restore(
            new TelegramNotificationId(row.Id),
            new UserId(row.OwnerId),
            new BotId(row.BotId),
            Enum.Parse<TelegramNotificationKind>(row.Kind),
            row.AlertId is { } alertId ? new BotAlertId(alertId) : null,
            JsonSerializer.Deserialize<Dictionary<string, string>>(row.Details) ?? [],
            row.CreatedAt,
            Enum.Parse<TelegramNotificationStatus>(row.Status),
            row.Attempts,
            row.NextAttemptAt,
            row.ClaimId is { } claimId ? new TelegramClaimId(claimId) : null,
            row.ClaimedUntil,
            row.SentAt,
            row.LastError);
    }

    public static TelegramNotificationDb ToDb(this TelegramNotification notification)
    {
        return new TelegramNotificationDb
        {
            Id = notification.Id.Value,
            OwnerId = notification.OwnerId.Value,
            BotId = notification.BotId.Value,
            Kind = notification.Kind.ToString(),
            AlertId = notification.AlertId?.Value,
            Details = JsonSerializer.Serialize(notification.Details),
            CreatedAt = notification.CreatedAt,
            Status = notification.Status.ToString(),
            Attempts = notification.Attempts,
            NextAttemptAt = notification.NextAttemptAt,
            ClaimId = notification.ClaimId?.Value,
            ClaimedUntil = notification.ClaimedUntil,
            SentAt = notification.SentAt,
            LastError = notification.LastError
        };
    }
}