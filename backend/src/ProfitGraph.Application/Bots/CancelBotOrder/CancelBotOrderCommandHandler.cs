using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.CancelBotOrder;

/// <summary>
///     Stores the owner's cancel for the bot's runner. The bot must run, and its last committed checkpoint must hold the order as
///     working and not already being cancelled; the runner checks again against the engine, which is the authority.
/// </summary>
internal sealed class CancelBotOrderCommandHandler(
    IBotRepository bots,
    IBotCheckpointRepository checkpoints,
    IOrderCancelRequestRepository cancels,
    TimeProvider clock) : IRequestHandler<CancelBotOrderCommand, OrderCancelInternal>
{
    public async Task<OrderCancelInternal> Handle(CancelBotOrderCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        Bot bot = await bots.GetOwned(command.BotId, command.OwnerId, cancellationToken);
        bot.EnsureAcceptsOrderCancels();
        OrderSnapshot? order = (await checkpoints.Get(bot.Id, cancellationToken))?.Engine.Account.Orders
            .FirstOrDefault(order => order.Id == command.EngineOrderId &&
                order.Status is OrderStatus.Active or OrderStatus.PendingSubmit or OrderStatus.PendingCancel);

        if (order is null)
        {
            throw new StateConflictException("The order is not working in the bot.", OrderCancelRequests.NotOpenCode);
        }

        if (order.Status == OrderStatus.PendingCancel)
        {
            throw new StateConflictException("The order is already being cancelled.", OrderCancelRequests.AlreadyCancellingCode);
        }

        if (await cancels.GetPending(bot.Id, cancellationToken) is { } waiting &&
            await cancels.GetForUpdate(waiting.Id, cancellationToken) is { } locked && locked.Expire(now))
        {
            await cancels.Update(locked, cancellationToken);
        }

        var request = OrderCancelRequest.Create(OrderCancelRequestId.New(), bot.Id, command.EngineOrderId, now);

        if (!await cancels.TryAdd(request, cancellationToken))
        {
            throw new StateConflictException("An order cancel of this bot is still waiting for the bot.", OrderCancelRequests.PendingCode);
        }

        return request.ToInternal(now);
    }
}