using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.GetBotUnresolvedOrders;

internal sealed class GetBotUnresolvedOrdersQueryHandler(IBotRepository bots, IOrderIntentRepository intents, IBotCheckpointRepository checkpoints, VenueRegistry venues)
    : IRequestHandler<GetBotUnresolvedOrdersQuery, IReadOnlyList<BotUnresolvedOrderInternal>>
{
    public async Task<IReadOnlyList<BotUnresolvedOrderInternal>> Handle(GetBotUnresolvedOrdersQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);
        BotCheckpoint? checkpoint = await checkpoints.Get(bot.Id, cancellationToken);
        IVenueDescriptor? venue = venues.Find(bot.Series.Venue.Value);

        return
        [
            .. (await intents.ListByBot(bot.Id, cancellationToken))
            .Where(intent => OrderAbsence.IsUnresolved(bot, intent, checkpoint, venue))
            .Select(intent => Unresolved(
                intent,
                OrderAbsence.Order(intent, checkpoint)!,
                OrderAbsence.CanConfirm(bot, intent, checkpoint, venue),
                OrderAbsence.IsAcceptedAndStopped(bot, intent, checkpoint, venue)))
        ];
    }

    private static BotUnresolvedOrderInternal Unresolved(OrderIntent intent, OrderSnapshot order, bool canConfirm, bool acceptedByVenue)
    {
        return new BotUnresolvedOrderInternal(
            intent.Id.Value,
            intent.EngineOrderId,
            order.Side,
            order.Type,
            order.Quantity,
            order.QuoteBudget,
            order.LimitPrice,
            intent.CreatedAt,
            intent.PlaceStartedAt,
            canConfirm,
            intent.ConfirmedAbsentAt,
            acceptedByVenue);
    }
}