using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Domain.Bots.OrderCancels;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.CancelBotOrder;

/// <summary>
///     Takes the owner's cancel. The bot must run or be stopped with no runner, and its last committed checkpoint must hold the order
///     as working and not already being cancelled. A running bot's request waits for its runner, which checks again against the
///     engine, the authority (ADR 0048). A stopped bot's engine does not change until the next start, so its order gets a cancel
///     intent at once, as a kill gives it (ADR 0055): the dispatcher sends it to the venue, and the start's reconciliation applies it.
/// </summary>
internal sealed class CancelBotOrderCommandHandler(
    IBotRepository bots,
    IBotLeaseRepository leases,
    IBotCheckpointRepository checkpoints,
    IOrderCancelRequestRepository cancels,
    IOrderIntentRepository intents,
    TimeProvider clock) : IRequestHandler<CancelBotOrderCommand, OrderCancelInternal>
{
    public async Task<OrderCancelInternal> Handle(CancelBotOrderCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        // Rows are locked lease, bot, intent everywhere; the locked bot keeps a start from opening it until the cancel is stored.
        BotLease? lease = await leases.GetForUpdate(new BotId(command.BotId), cancellationToken);
        Bot bot = await bots.GetOwnedForUpdate(command.BotId, command.OwnerId, cancellationToken);
        bool leaseHeld = lease is not null && !lease.IsExpired(now);
        bot.EnsureAcceptsOrderCancels(leaseHeld);
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

        if (bot.IsStoppedWithoutRunner(leaseHeld))
        {
            return await CancelAtVenue(bot, order, now, cancellationToken);
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

    /// <summary>
    ///     Stores the stopped bot's cancel as requested, with one cancel intent for the order. An order already cancelled by a kill
    ///     or an earlier cancel, and a submit that never left the process (it waits for the start), refuse. A deferred submit is
    ///     cancelled by the dispatcher locally (ADR 0024).
    /// </summary>
    private async Task<OrderCancelInternal> CancelAtVenue(Bot bot, OrderSnapshot order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        IReadOnlyList<OrderIntent> stored = await intents.ListByBot(bot.Id, cancellationToken);

        if (stored.Any(intent => intent.Kind == OrderIntentKind.Cancel && intent.EngineOrderId == order.Id))
        {
            throw new StateConflictException("The order is already being cancelled.", OrderCancelRequests.AlreadyCancellingCode);
        }

        OrderIntent? submit = stored.FirstOrDefault(intent => intent.Kind == OrderIntentKind.Submit && intent.EngineOrderId == order.Id);

        if (submit is null || (submit.PlaceStartedAt is null && submit.Status is not (OrderIntentStatus.Sent or OrderIntentStatus.Deferred)))
        {
            throw new StateConflictException("The order never left for the venue; it waits for the bot's start.", OrderCancelRequests.NotSentCode);
        }

        var request = OrderCancelRequest.Create(OrderCancelRequestId.New(), bot.Id, order.Id, now);
        request.MarkRequested(now);

        if (!await intents.TryAdd(OrderIntent.Cancel(OrderIntentId.New(), bot.Id, order.Id, submit.ClientOrderId, fromKill: false, now), cancellationToken))
        {
            throw new StateConflictException("The order is already being cancelled.", OrderCancelRequests.AlreadyCancellingCode);
        }

        await cancels.TryAdd(request, cancellationToken);

        return request.ToInternal(now);
    }
}