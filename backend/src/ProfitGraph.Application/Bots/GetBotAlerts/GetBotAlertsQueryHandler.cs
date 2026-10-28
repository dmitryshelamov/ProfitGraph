using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Alerts;

namespace ProfitGraph.Application.Bots.GetBotAlerts;

internal sealed class GetBotAlertsQueryHandler(IBotRepository bots, IBotAlertRepository alerts) : IRequestHandler<GetBotAlertsQuery, IReadOnlyList<BotAlertInternal>>
{
    public async Task<IReadOnlyList<BotAlertInternal>> Handle(GetBotAlertsQuery query, CancellationToken cancellationToken)
    {
        BotId? botId = query.BotId is { } id ? (await bots.GetOwned(id, query.UserId, cancellationToken)).Id : null;
        UserId? ownerId = query.EveryUser && botId is null ? null : new UserId(query.UserId);
        IReadOnlyList<BotAlert> listed = await alerts.List(ownerId, botId, query.UnacknowledgedOnly, query.Limit, cancellationToken);

        return [.. listed.Select(alert => alert.ToInternal())];
    }
}