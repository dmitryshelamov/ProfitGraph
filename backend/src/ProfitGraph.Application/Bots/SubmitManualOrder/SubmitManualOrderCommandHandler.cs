using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.ManualOrders;

namespace ProfitGraph.Application.Bots.SubmitManualOrder;

internal sealed class SubmitManualOrderCommandHandler(IBotRepository bots, IManualOrderRequestRepository manualOrders, TimeProvider clock)
    : IRequestHandler<SubmitManualOrderCommand, ManualOrderInternal>
{
    public async Task<ManualOrderInternal> Handle(SubmitManualOrderCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        Bot bot = await bots.GetOwned(command.BotId, command.OwnerId, cancellationToken);
        bot.EnsureAcceptsManualOrders();

        if (await manualOrders.GetPending(bot.Id, cancellationToken) is { } waiting &&
            await manualOrders.GetForUpdate(waiting.Id, cancellationToken) is { } locked && locked.Expire(now))
        {
            await manualOrders.Update(locked, cancellationToken);
        }

        ManualOrderRequest request = command.LimitPrice is { } limitPrice
            ? ManualOrderRequest.CreateLimit(ManualOrderRequestId.New(), bot.Id, command.Side, command.Amount, limitPrice, now)
            : ManualOrderRequest.Create(ManualOrderRequestId.New(), bot.Id, command.Side, command.Amount, now);

        if (!await manualOrders.TryAdd(request, cancellationToken))
        {
            throw new StateConflictException("A manual order of this bot is still waiting for the bot.", ManualOrderRequests.PendingCode);
        }

        return request.ToInternal(now);
    }
}