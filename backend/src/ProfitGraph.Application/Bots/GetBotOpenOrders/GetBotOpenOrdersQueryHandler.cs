using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.GetBotOpenOrders;

/// <summary>
///     Reads the open orders from the bot's last checkpoint. A stopped bot's checkpoint does not change until its next start, so its
///     cancel intents (an owner's cancel, ADR 0055, or a kill) are laid over it: an order whose cancel the venue took is no longer
///     listed, and one whose cancel still waits reads as being cancelled.
/// </summary>
internal sealed class GetBotOpenOrdersQueryHandler(IBotRepository bots, IBotCheckpointRepository checkpoints, IOrderIntentRepository intents)
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

        Dictionary<long, OrderIntentStatus> cancelled = bot.DesiredState == BotDesiredState.Stopped
            ? (await intents.ListByBot(bot.Id, cancellationToken)).Where(intent => intent.Kind == OrderIntentKind.Cancel)
            .ToDictionary(intent => intent.EngineOrderId, intent => intent.Status)
            : [];

        return
        [
            .. account.Orders
                .Where(order => order.Status is OrderStatus.Active or OrderStatus.PendingSubmit or OrderStatus.PendingCancel)
                .Where(order => !cancelled.TryGetValue(order.Id, out OrderIntentStatus cancel) || cancel != OrderIntentStatus.Sent)
                .OrderBy(order => order.Id)
                .Select(order => new BotOpenOrderInternal(
                    order.Id,
                    order.Tag,
                    order.Side,
                    order.Type,
                    cancelled.ContainsKey(order.Id) ? OrderStatus.PendingCancel : order.Status,
                    order.Quantity,
                    order.LimitPrice,
                    order.QuoteBudget,
                    account.Progress.FirstOrDefault(progress => progress.OrderId == order.Id)?.GrossBase ?? 0m))
        ];
    }
}
