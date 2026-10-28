using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.GetOrderCancel;

internal sealed class GetOrderCancelQueryHandler(IBotRepository bots, IOrderCancelRequestRepository cancels, TimeProvider clock)
    : IRequestHandler<GetOrderCancelQuery, OrderCancelInternal>
{
    public async Task<OrderCancelInternal> Handle(GetOrderCancelQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);
        OrderCancelRequest? request = query.Id == Guid.Empty ? null : await cancels.Get(new OrderCancelRequestId(query.Id), cancellationToken);

        return request is not null && request.BotId == bot.Id
            ? request.ToInternal(clock.GetUtcNow())
            : throw new ResourceNotFoundException("The order cancel does not exist.", OrderCancelRequests.NotFoundCode);
    }
}