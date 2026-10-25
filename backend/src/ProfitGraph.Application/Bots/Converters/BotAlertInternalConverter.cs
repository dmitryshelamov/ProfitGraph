using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Converters;

internal static class BotAlertInternalConverter
{
    public static BotAlertInternal ToInternal(this BotAlert alert)
    {
        return new BotAlertInternal(alert.Id.Value, alert.BotId.Value, alert.Kind, alert.Reason, alert.CreatedAt, alert.AcknowledgedAt);
    }
}