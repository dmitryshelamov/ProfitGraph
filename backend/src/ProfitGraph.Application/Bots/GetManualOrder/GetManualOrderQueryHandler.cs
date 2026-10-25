using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.GetManualOrder;

internal sealed class GetManualOrderQueryHandler(IBotRepository bots, IManualOrderRequestRepository manualOrders, TimeProvider clock)
    : IRequestHandler<GetManualOrderQuery, ManualOrderInternal>
{
    public async Task<ManualOrderInternal> Handle(GetManualOrderQuery query, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(query.BotId, query.OwnerId, cancellationToken);
        ManualOrderRequest? request = query.Id == Guid.Empty ? null : await manualOrders.Get(new ManualOrderRequestId(query.Id), cancellationToken);

        return request is not null && request.BotId == bot.Id
            ? request.ToInternal(clock.GetUtcNow())
            : throw new ResourceNotFoundException("The manual order does not exist.", ManualOrderRequests.NotFoundCode);
    }
}