using System.Collections.Concurrent;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotOrdersToRefresh;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     One <c>OrdersStreamService/OrderStateStream</c> subscription per running bot, for the bot's account (ADR 0023). A
///     message is only a signal: an order of the bot, known by its request ID in the attempts table, is read again with
///     <c>GetOrderState</c> and published; another order on the bot's share is foreign unless the bot allows it. A lost
///     stream is subscribed again under <see cref="PrivateStreamReconnectPolicy" /> (ADR 0044): once T-Invest confirmed the
///     new subscription, the bot's open orders and the account's active orders on the share are read again, so nothing that
///     changed while it was lost is missed. A stream that stays lost for the whole budget, an unreadable state, a failed read
///     or a foreign order ends the bot's updates with that error, so its runner restarts and reconciles again.
/// </summary>
internal sealed class TInvestOrderStreams(
    TInvestTransport transport,
    TInvestOrderReads reads,
    ITInvestOrderAttempts attempts,
    TInvestOrderUpdates updates,
    TInvestOrderProgress progress,
    IServiceScopeFactory scopes,
    PrivateStreamReconnectPolicy policy,
    IOptions<TInvestOptions> options,
    TimeProvider clock,
    ILogger<TInvestOrderStreams> logger)
{
    private const int MaxReads = 5;

    private readonly ConcurrentDictionary<BotId, TInvestOrderStream> _streams = new ConcurrentDictionary<BotId, TInvestOrderStream>();

    /// <summary>
    ///     Opens the bot's subscription, replacing an earlier one, and starts its updates afresh. Returns once T-Invest
    ///     confirmed it, before the bot's orders are looked up, so no change between the lookup and the stream is missed.
    /// </summary>
    public async Task Open(TInvestBotContext context, bool allowForeignOrders, CancellationToken cancellationToken)
    {
        await Close(context.BotId);
        updates.Reset(context.BotId);
        var subscribed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = new CancellationTokenSource();
        Task reading = Task.Run(() => Keep(context, allowForeignOrders, subscribed, stop.Token), CancellationToken.None);
        _streams[context.BotId] = new TInvestOrderStream(stop, reading);

        try
        {
            await subscribed.Task.WaitAsync(options.Value.RequestTimeout, cancellationToken);
        }
        catch
        {
            await Close(context.BotId);

            throw;
        }
    }

    /// <summary>
    ///     Whether the bot's runner in this process reconciled and reads its updates now.
    /// </summary>
    public bool IsOpen(BotId botId)
    {
        return _streams.ContainsKey(botId);
    }

    public async Task Close(BotId botId)
    {
        if (_streams.TryRemove(botId, out TInvestOrderStream? stream))
        {
            await stream.Stop();
        }
    }

    /// <summary>
    ///     Reads the subscription until the runner closes it. The first one must be confirmed for reconciliation to go on; a
    ///     confirmed one is subscribed again each time it is lost while the outage lasts less than the budget.
    /// </summary>
    private async Task Keep(TInvestBotContext context, bool allowForeignOrders, TaskCompletionSource subscribed, CancellationToken cancellationToken)
    {
        var outage = new PrivateStreamOutage(policy, clock);

        try
        {
            Exception lost = await Read(context, allowForeignOrders, subscribed, restoring: null, cancellationToken);

            if (!subscribed.Task.IsCompletedSuccessfully)
            {
                throw lost;
            }

            while (true)
            {
                if (outage.Lost() is not { } delay)
                {
                    logger.LogWarning(
                        "The order state stream of bot {BotId} stayed lost with {ExceptionType}; its updates end.",
                        context.BotId.Value,
                        lost.GetType().Name);
                    updates.Fail(context.BotId, lost);

                    return;
                }

                logger.LogWarning(
                    "The order state stream of bot {BotId} was lost with {ExceptionType}; it subscribes again in {Delay}.",
                    context.BotId.Value,
                    lost.GetType().Name,
                    delay);
                await Task.Delay(delay, clock, cancellationToken);
                lost = await Read(context, allowForeignOrders, subscribed, outage, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The bot's runner ended or reconciles again.
            subscribed.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "The order state stream of bot {BotId} ended with {ExceptionType}, reason {Reason}.",
                context.BotId.Value,
                exception.GetType().Name,
                (exception as TInvestOrderUnresolvedException)?.Reason);
            subscribed.TrySetException(exception);
            updates.Fail(context.BotId, exception);
        }
    }

    /// <summary>
    ///     Subscribes to the account's order states and handles them until the stream is lost, and returns why. A restoring
    ///     subscription must be confirmed within the request timeout and reads the bot's orders again before any state; a
    ///     refusal, or a read again that fails, is a loss. A state that cannot be handled throws.
    /// </summary>
    private async Task<Exception> Read(
        TInvestBotContext context,
        bool allowForeignOrders,
        TaskCompletionSource subscribed,
        PrivateStreamOutage? restoring,
        CancellationToken cancellationToken)
    {
        using var confirming = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (restoring is not null)
        {
            confirming.CancelAfter(options.Value.RequestTimeout);
        }

        await using IAsyncEnumerator<OrderStateStreamResponse> messages = transport.Stream(
                context.Token,
                TInvestMethodGroup.Orders,
                (invoker, callOptions) => new OrdersStreamService.OrdersStreamServiceClient(invoker).OrderStateStream(
                    new OrderStateStreamRequest { Accounts = { context.AccountId } },
                    callOptions),
                confirming.Token)
            .GetAsyncEnumerator(confirming.Token);

        while (true)
        {
            try
            {
                if (!await messages.MoveNextAsync())
                {
                    return new IOException("T-Invest closed the order state stream.");
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                return exception;
            }

            OrderStateStreamResponse message = messages.Current;

            if (message.Subscription is { } subscription)
            {
                if (subscription.Status != ResultSubscriptionStatus.Ok)
                {
                    return new IOException("T-Invest refused the order state subscription.");
                }

                if (restoring is not null)
                {
                    confirming.CancelAfter(Timeout.InfiniteTimeSpan);

                    try
                    {
                        await Refresh(context, allowForeignOrders, cancellationToken);
                    }
                    catch (Exception exception) when (exception is not OrderGatewaySafetyException && !cancellationToken.IsCancellationRequested)
                    {
                        return exception;
                    }

                    restoring.Restored();
                    restoring = null;
                    logger.LogInformation("The order state stream of bot {BotId} was restored.", context.BotId.Value);
                }

                subscribed.TrySetResult();
            }

            if (message.OrderState is { } order && order.AccountId == context.AccountId && order.InstrumentUid == context.Share.Uid)
            {
                await Signal(context, allowForeignOrders, order, cancellationToken);
            }
        }
    }

    /// <summary>
    ///     After T-Invest confirmed a restored subscription, checks the account's active orders on the share and reads again,
    ///     by request ID as reconciliation does, every attempt that may be working for the orders the bot's engine and intents
    ///     hold open, and publishes what T-Invest reports. An order T-Invest does not find is left as it is: an empty answer
    ///     proves nothing, and the stream or the next reconciliation tells.
    /// </summary>
    private async Task Refresh(TInvestBotContext context, bool allowForeignOrders, CancellationToken cancellationToken)
    {
        if (!allowForeignOrders)
        {
            OrderState[] onShare = [.. (await reads.Active(context, cancellationToken)).Where(order => order.InstrumentUid == context.Share.Uid)];
            Guid[] requestIds = [.. onShare.Select(order => Guid.TryParse(order.OrderRequestId, out Guid id) ? id : Guid.Empty).Where(id => id != Guid.Empty)];
            IReadOnlyDictionary<Guid, Guid> owners = await attempts.Owners(requestIds, cancellationToken);

            if (onShare.Any(order => !Guid.TryParse(order.OrderRequestId, out Guid id) || !owners.TryGetValue(id, out Guid owner) || owner != context.BotId.Value))
            {
                throw new OrderGatewaySafetyException(BotStatusReason.ForeignOrder, "Another source trades on the bot's share.");
            }
        }

        BotReconcileInternal? request;

        using (IServiceScope scope = scopes.CreateScope())
        {
            request = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetBotOrdersToRefreshQuery(context.BotId.Value), cancellationToken);
        }

        foreach (ReconcileOrderInternal order in request?.Orders ?? [])
        {
            if (await attempts.Latest(context.BotId.Value, order.EngineOrderId, cancellationToken) is
                    { State: TInvestOrderAttemptState.Sent or TInvestOrderAttemptState.Active or TInvestOrderAttemptState.Unknown } attempt &&
                await reads.ByRequestId(context, attempt.RequestId, cancellationToken) is { } state)
            {
                updates.Publish(context.BotId, await progress.Apply(context, attempt, state, cancellationToken));
            }
        }
    }

    private async Task Signal(TInvestBotContext context, bool allowForeignOrders, OrderStateStreamResponse.Types.OrderState order, CancellationToken cancellationToken)
    {
        TInvestOrderAttempt? attempt = Guid.TryParse(order.OrderRequestId, out Guid requestId)
            ? await attempts.ByRequestId(requestId, cancellationToken)
            : null;

        if (attempt is null || attempt.BotId != context.BotId.Value)
        {
            if (!allowForeignOrders)
            {
                throw new OrderGatewaySafetyException(BotStatusReason.ForeignOrder, "Another source trades on the bot's share.");
            }

            return;
        }

        for (var read = 1;; read++)
        {
            // The stream tells of an order a moment before the order service answers for its request ID (ADR 0036): an order that
            // is not found yet is read again, and only one that stays missing for every read is unresolved.
            if (await reads.ByRequestId(context, attempt.RequestId, cancellationToken) is not { } state)
            {
                if (read >= MaxReads)
                {
                    throw new TInvestOrderUnresolvedException("OrderNotFound");
                }

                await Task.Delay(options.Value.OrderReadRetryDelay, cancellationToken);

                continue;
            }

            try
            {
                updates.Publish(context.BotId, await progress.Apply(context, attempt, state, cancellationToken));

                return;
            }
            catch (TInvestOrderUnresolvedException unresolved) when (unresolved.IsTransient && read < MaxReads)
            {
                await Task.Delay(options.Value.OrderReadRetryDelay, cancellationToken);
            }
        }
    }
}