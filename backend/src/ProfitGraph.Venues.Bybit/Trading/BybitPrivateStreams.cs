using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     One private Bybit connection per running bot (ADR 0017). A message naming one of the bot's orders looks the order
///     up and publishes its state; an order or execution on the bot's symbol that is not the bot's is foreign unless the
///     bot allows it. A lost connection, an unreadable message or a failed lookup ends the bot's updates with that error,
///     so its runner restarts and reconciles again.
/// </summary>
internal sealed class BybitPrivateStreams(
    IBybitPrivateStreamTransport transport,
    BybitReadClient read,
    BybitOrderReads reads,
    BybitOrderUpdates updates,
    ILogger<BybitPrivateStreams> logger)
{
    private static readonly TimeSpan AuthLifetime = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<BotId, BybitPrivateStream> _streams = new ConcurrentDictionary<BotId, BybitPrivateStream>();

    /// <summary>
    ///     Opens the bot's connection, replacing an earlier one, and starts its updates afresh. Returns once Bybit
    ///     confirmed it, before the bot's orders are looked up, so no change between the lookup and the stream is missed.
    /// </summary>
    public async Task Open(BybitBotContext context, bool allowForeignOrders, CancellationToken cancellationToken)
    {
        await Close(context.BotId);
        long serverTime = await read.ServerTime(cancellationToken) ?? throw new BybitOrderUnresolvedException();
        IBybitPrivateConnection connection = await transport.Connect(context.Credential, serverTime + (long)AuthLifetime.TotalMilliseconds, cancellationToken);
        updates.Reset(context.BotId);
        var stream = new BybitPrivateStream(connection);
        _streams[context.BotId] = stream;
        stream.Start((opened, token) => Read(context, allowForeignOrders, opened, token));
    }

    public async Task Close(BotId botId)
    {
        if (_streams.TryRemove(botId, out BybitPrivateStream? stream))
        {
            await stream.Stop();
        }
    }

    private async Task Read(BybitBotContext context, bool allowForeignOrders, IBybitPrivateConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (string message in connection.Messages(cancellationToken))
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

            throw new IOException("Bybit closed the private stream.");
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
}