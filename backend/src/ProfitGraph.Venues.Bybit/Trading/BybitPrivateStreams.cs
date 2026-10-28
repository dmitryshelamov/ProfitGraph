using System.Collections.Concurrent;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.GetBotOrdersToRefresh;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     One private Bybit connection per running bot (ADR 0017). A message naming one of the bot's orders looks the order
///     up and publishes its state; an order or execution on the bot's symbol that is not the bot's is foreign unless the
///     bot allows it. A lost connection is restored under <see cref="PrivateStreamReconnectPolicy" /> (ADR 0044): once Bybit
///     confirmed the new one, the bot's open orders and what trades on its symbol are read again, so nothing that changed
///     while it was lost is missed. A connection that stays lost for the whole budget, an unreadable message, a failed lookup
///     or a foreign order ends the bot's updates with that error, so its runner restarts and reconciles again.
/// </summary>
internal sealed class BybitPrivateStreams(
    IBybitPrivateStreamTransport transport,
    BybitReadClient read,
    BybitOrderReads reads,
    BybitOrderUpdates updates,
    IServiceScopeFactory scopes,
    PrivateStreamReconnectPolicy policy,
    BybitOptions options,
    TimeProvider clock,
    ILogger<BybitPrivateStreams> logger)
{
    private readonly ConcurrentDictionary<BotId, BybitPrivateStream> _streams = new ConcurrentDictionary<BotId, BybitPrivateStream>();

    /// <summary>
    ///     Opens the bot's connection, replacing an earlier one, and starts its updates afresh. Returns once Bybit
    ///     confirmed it, before the bot's orders are looked up, so no change between the lookup and the stream is missed.
    /// </summary>
    public async Task Open(BybitBotContext context, bool allowForeignOrders, CancellationToken cancellationToken)
    {
        await Close(context.BotId);
        IBybitPrivateConnection connection = await Connect(context, cancellationToken);
        updates.Reset(context.BotId);
        var stream = new BybitPrivateStream(connection);
        _streams[context.BotId] = stream;
        stream.Start((opened, token) => Keep(context, allowForeignOrders, opened, token));
    }

    public async Task Close(BotId botId)
    {
        if (_streams.TryRemove(botId, out BybitPrivateStream? stream))
        {
            await stream.Stop();
        }
    }

    private async Task<IBybitPrivateConnection> Connect(BybitBotContext context, CancellationToken cancellationToken)
    {
        long serverTime = await read.ServerTime(cancellationToken) ?? throw new BybitOrderUnresolvedException();

        return await transport.Connect(context.Credential, serverTime + (long)options.PrivateStreamAuthLifetime.TotalMilliseconds, cancellationToken);
    }

    /// <summary>
    ///     Reads the connection until the runner closes it, restoring it each time it is lost while the outage lasts less than
    ///     the budget.
    /// </summary>
    private async Task Keep(BybitBotContext context, bool allowForeignOrders, BybitPrivateStream stream, CancellationToken cancellationToken)
    {
        var outage = new PrivateStreamOutage(policy, clock);

        try
        {
            while (true)
            {
                Exception lost = await Read(context, allowForeignOrders, stream.Connection!, cancellationToken);
                await stream.Replace(next: null);

                while (true)
                {
                    if (outage.Lost() is not { } delay)
                    {
                        logger.LogWarning(
                            "The private stream of bot {BotId} stayed lost with {ExceptionType}; its updates end.",
                            context.BotId.Value,
                            lost.GetType().Name);
                        updates.Fail(context.BotId, lost);

                        return;
                    }

                    logger.LogWarning(
                        "The private stream of bot {BotId} was lost with {ExceptionType}; it connects again in {Delay}.",
                        context.BotId.Value,
                        lost.GetType().Name,
                        delay);
                    await Task.Delay(delay, clock, cancellationToken);

                    try
                    {
                        await stream.Replace(await Connect(context, cancellationToken));
                        await Refresh(context, allowForeignOrders, cancellationToken);
                        outage.Restored();
                        logger.LogInformation("The private stream of bot {BotId} was restored.", context.BotId.Value);

                        break;
                    }
                    catch (Exception exception) when (exception is not OrderGatewaySafetyException && !cancellationToken.IsCancellationRequested)
                    {
                        await stream.Replace(next: null);
                        lost = exception;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The bot's runner ended or reconciles again.
        }
        catch (Exception exception)
        {
            logger.LogWarning("The private stream of bot {BotId} ended with {ExceptionType}.", context.BotId.Value, exception.GetType().Name);
            updates.Fail(context.BotId, exception);
        }
    }

    /// <summary>
    ///     Handles the connection's messages until it is lost, and returns why. A message that cannot be handled throws.
    /// </summary>
    private async Task<Exception> Read(BybitBotContext context, bool allowForeignOrders, IBybitPrivateConnection connection, CancellationToken cancellationToken)
    {
        await using IAsyncEnumerator<string> messages = connection.Messages(cancellationToken).GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            try
            {
                if (!await messages.MoveNextAsync())
                {
                    return new IOException("Bybit closed the private stream.");
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                return exception;
            }

            await Handle(context, allowForeignOrders, messages.Current, cancellationToken);
        }
    }

    private async Task Handle(BybitBotContext context, bool allowForeignOrders, string message, CancellationToken cancellationToken)
    {
        foreach ((string clientOrderId, string symbol) in BybitPrivateStreamMessages.Orders(message))
        {
            if (symbol != context.Symbol)
            {
                continue;
            }

            if (BybitClientOrderIds.TryParse(clientOrderId, out BotId? owner, out long engineOrderId) && owner == context.BotId)
            {
                BybitSpotOrder order = await reads.LookUp(context, clientOrderId, cancellationToken) ?? throw new BybitOrderUnresolvedException();
                updates.Publish(context.BotId, BybitOrderTranslation.Updates(engineOrderId, order, context.Instrument.BaseAsset, context.Instrument.QuoteAsset));
            }
            else if (!allowForeignOrders)
            {
                throw new OrderGatewaySafetyException(BotStatusReason.ForeignOrder, "Another source trades on the bot's symbol.");
            }
        }
    }

    /// <summary>
    ///     After Bybit confirmed a restored connection, checks what trades on the bot's symbol and reads again every order the
    ///     bot's engine and intents hold open, with the lookups reconciliation uses, and publishes what Bybit reports. An order
    ///     Bybit does not know is left as it is: an empty answer proves nothing, and the stream or the next reconciliation tells.
    /// </summary>
    private async Task Refresh(BybitBotContext context, bool allowForeignOrders, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> onSymbol = await reads.SymbolClientOrderIds(context, cancellationToken);

        if (!allowForeignOrders &&
            onSymbol.Any(clientOrderId => !BybitClientOrderIds.TryParse(clientOrderId, out BotId? owner, out _) || owner != context.BotId))
        {
            throw new OrderGatewaySafetyException(BotStatusReason.ForeignOrder, "Another source trades on the bot's symbol.");
        }

        BotReconcileInternal? request;

        using (IServiceScope scope = scopes.CreateScope())
        {
            request = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetBotOrdersToRefreshQuery(context.BotId.Value), cancellationToken);
        }

        foreach (ReconcileOrderInternal order in request?.Orders ?? [])
        {
            if (await reads.LookUp(context, order.ClientOrderId.Value, cancellationToken) is { } found)
            {
                updates.Publish(context.BotId, BybitOrderTranslation.Updates(order.EngineOrderId, found, context.Instrument.BaseAsset, context.Instrument.QuoteAsset));
            }
        }
    }
}