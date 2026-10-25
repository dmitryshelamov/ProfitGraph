using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Api.Bots.Converters;

internal static class BotAlertResponseConverter
{
    public static BotAlertResponse ToResponse(this BotAlertInternal alert)
    {
        return new BotAlertResponse(alert.Id, alert.BotId, alert.Kind, alert.Reason, alert.CreatedAt, alert.AcknowledgedAt);
    }
}