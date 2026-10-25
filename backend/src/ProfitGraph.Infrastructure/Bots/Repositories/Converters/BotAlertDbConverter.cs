using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class BotAlertDbConverter
{
    public static BotAlert ToDomain(this BotAlertDb row)
    {
        return BotAlert.Restore(
            new BotAlertId(row.Id),
            new BotId(row.BotId),
            new UserId(row.OwnerId),
            Enum.Parse<BotAlertKind>(row.Kind),
            row.Reason,
            row.CreatedAt,
            row.AcknowledgedAt);
    }

    public static BotAlertDb ToDb(this BotAlert alert)
    {
        return new BotAlertDb
        {
            Id = alert.Id.Value,
            BotId = alert.BotId.Value,
            OwnerId = alert.OwnerId.Value,
            Kind = alert.Kind.ToString(),
            Reason = alert.Reason,
            CreatedAt = alert.CreatedAt,
            AcknowledgedAt = alert.AcknowledgedAt
        };
    }
}