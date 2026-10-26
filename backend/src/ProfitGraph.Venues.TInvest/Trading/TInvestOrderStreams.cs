using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     One <c>OrdersStreamService/OrderStateStream</c> subscription per running bot, for the bot's account (ADR 0023). A
///     message is only a signal: an order of the bot, known by its request ID in the attempts table, is read again with
///     <c>GetOrderState</c> and published; another order on the bot's share is foreign unless the bot allows it. A lost
///     stream, an unreadable state or a failed read ends the bot's updates with that error, so its runner restarts and
///     reconciles again.
/// </summary>
internal sealed class TInvestOrderStreams(
    TInvestTransport transport,
    TInvestOrderReads reads,
    ITInvestOrderAttempts attempts,
    TInvestOrderUpdates updates,
    TInvestOrderProgress progress,
    IOptions<TInvestOptions> options,
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
        Task reading = Task.Run(() => Read(context, allowForeignOrders, subscribed, stop.Token), CancellationToken.None);
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

    private async Task Read(TInvestBotContext context, bool allowForeignOrders, TaskCompletionSource subscribed, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (OrderStateStreamResponse message in transport.Stream(
                               context.Token,
                               TInvestMethodGroup.Orders,
                               (invoker, callOptions) => new OrdersStreamService.OrdersStreamServiceClient(invoker).OrderStateStream(
                                   new OrderStateStreamRequest { Accounts = { context.AccountId } },
                                   callOptions),
                               cancellationToken))
            {
                if (message.Subscription is { } subscription)
                {
                    if (subscription.Status != ResultSubscriptionStatus.Ok)
                    {
                        throw new IOException("T-Invest refused the order state subscription.");
                    }

                    subscribed.TrySetResult();
                }

                if (message.OrderState is { } order && order.AccountId == context.AccountId && order.InstrumentUid == context.Share.Uid)
                {
                    await Signal(context, allowForeignOrders, order, cancellationToken);
                }
            }

            throw new IOException("T-Invest closed the order state stream.");
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
            OrderState state = await reads.ByRequestId(context, attempt.RequestId, cancellationToken) ?? throw new TInvestOrderUnresolvedException("OrderNotFound");

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