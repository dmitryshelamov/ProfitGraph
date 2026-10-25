using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.GetBotWaitingOrders;

internal sealed class GetBotWaitingOrdersQueryHandler(IBotRepository bots, IBotOrderWaitingRepository waiting)
    : IRequestHandler<GetBotWaitingOrdersQuery, IReadOnlyList<BotOrderWaitingInternal>>
{
    public async Task<IReadOnlyList<BotOrderWaitingInternal>> Handle(GetBotWaitingOrdersQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);

        return
        [
            .. (await waiting.List(bot.Id, cancellationToken))
            .Select(order => new BotOrderWaitingInternal(order.EngineOrderId, order.Code, order.Since, order.Until, order.Low, order.High))
        ];
    }
}