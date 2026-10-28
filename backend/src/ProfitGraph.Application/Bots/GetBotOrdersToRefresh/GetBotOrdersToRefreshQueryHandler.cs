using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.OrderIntents;

namespace ProfitGraph.Application.Bots.GetBotOrdersToRefresh;

internal sealed class GetBotOrdersToRefreshQueryHandler(
    IBotRepository bots,
    IBotCheckpointRepository checkpoints,
    IOrderIntentRepository intents,
    VenuePorts<IOrderGateway> gateways) : IRequestHandler<GetBotOrdersToRefreshQuery, BotReconcileInternal?>
{
    public async Task<BotReconcileInternal?> Handle(GetBotOrdersToRefreshQuery query, CancellationToken cancellationToken)
    {
        var botId = new BotId(query.BotId);

        if (await bots.Get(botId, cancellationToken) is not { } bot || gateways.Find(bot.Series.Venue) is not { } gateway)
        {
            return null;
        }

        BotCheckpoint? checkpoint = await checkpoints.Get(botId, cancellationToken);

        return ReconcileRequests.For(bot, checkpoint?.Engine, gateway, await intents.ListByBot(botId, cancellationToken));
    }
}