using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.GetBotOpenOrders;

internal sealed class GetBotOpenOrdersQueryHandler(IBotRepository bots, IBotCheckpointRepository checkpoints)
    : IRequestHandler<GetBotOpenOrdersQuery, IReadOnlyList<BotOpenOrderInternal>>
{
    public async Task<IReadOnlyList<BotOpenOrderInternal>> Handle(GetBotOpenOrdersQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);
        ExternalAccountCheckpoint? account = (await checkpoints.Get(bot.Id, cancellationToken))?.Engine.Account;

        if (account is null)
        {
            return [];
        }

        return
        [
            .. account.Orders
                .Where(order => order.Status is OrderStatus.Active or OrderStatus.PendingSubmit or OrderStatus.PendingCancel)
                .OrderBy(order => order.Id)
                .Select(order => new BotOpenOrderInternal(
                    order.Id,
                    order.Tag,
                    order.Side,
                    order.Type,
                    order.Status,
                    order.Quantity,
                    order.LimitPrice,
                    order.QuoteBudget,
                    account.Progress.FirstOrDefault(progress => progress.OrderId == order.Id)?.GrossBase ?? 0m))
        ];
    }
}
