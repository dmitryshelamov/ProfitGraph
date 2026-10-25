using System.Globalization;
using System.Text.Json;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Bybit Spot order execution (ADR 0017, Profit Fox Designer ADR 0055). Registered only by a Worker with the live role
///     and trading enabled. A first attempt places; a later attempt, or a duplicate response, looks the order up by its
///     client ID and never places again on an empty answer. Only an explicit Bybit rejection rejects; every unknown
///     outcome is an exception, so the intent retries and eventually degrades the bot.
/// </summary>
internal sealed class BybitOrderGateway(BybitTradeClient trade, BybitReadClient read, BybitBotContexts contexts, BybitOrderUpdates updates) : IOrderGateway
{
    public VenueId Venue => BybitVenue.VenueId;

    public ClientOrderId ClientOrderIdFor(BotId botId, long engineOrderId)
    {
        return BybitClientOrderIds.For(botId, engineOrderId);
    }

    public IAsyncEnumerable<ExternalOrderUpdate> Updates(BotId botId, CancellationToken cancellationToken)
    {
        return updates.Read(botId, cancellationToken);
    }

    public async Task<ExternalOrderUpdate?> Submit(OrderSubmitInternal submit, Func<CancellationToken, Task> markPlaceStarted, CancellationToken cancellationToken)
    {
        BybitBotContext context = await contexts.Get(submit.BotId, cancellationToken);

        if (submit.MustLookUpBeforeSubmit)
        {
            await PublishExisting(context, submit.ClientOrderId.Value, cancellationToken);

            return null;
        }

        decimal? lastPrice = submit.Order is { Type: OrderType.Market, Side: OrderSide.Sell } ? await LastPrice(context.Symbol, cancellationToken) : null;
        string? body = BybitOrderRequests.Create(context, submit.ClientOrderId.Value, submit.Order, lastPrice);

        if (body is null)
        {
            return ExternalOrderUpdate.Reject(submit.Order.Id, RejectionReason.BelowMinNotional, "The order is below Bybit Spot minimums.");
        }

        BybitRestResult placed = await trade.Post(context.Credential, "/v5/order/create", body, markPlaceStarted, cancellationToken);

        switch (placed.Outcome)
        {
            case BybitRestOutcome.Accepted:
                return null;
            case BybitRestOutcome.Rejected:
                return ExternalOrderUpdate.Reject(
                    submit.Order.Id,
                    Rejection(placed.Code, submit.Order.Side),
                    string.Create(CultureInfo.InvariantCulture, $"Bybit rejected the order ({placed.Code})."));
            case BybitRestOutcome.Duplicate:
                await PublishExisting(context, submit.ClientOrderId.Value, cancellationToken);

                return null;
            default:
                throw new BybitOrderUnresolvedException();
        }
    }

    public async Task Cancel(BotId botId, ClientOrderId clientOrderId, CancellationToken cancellationToken)
    {
        BybitBotContext context = await contexts.Get(botId, cancellationToken);
        string body = JsonSerializer.Serialize(new { category = "spot", symbol = context.Symbol, orderLinkId = clientOrderId.Value });
        BybitRestResult cancelled = await trade.Post(context.Credential, "/v5/order/cancel", body, beforeSend: null, cancellationToken);

        if (cancelled.Outcome == BybitRestOutcome.Accepted)
        {
            return;
        }

        if (!(await PublishExisting(context, clientOrderId.Value, cancellationToken)).IsFinal)
        {
            throw new BybitOrderUnresolvedException();
        }
    }

    /// <summary>
    ///     Finds the order by its client ID in open, then in historical orders, and publishes its state. An empty answer is
    ///     not proof that the order was never placed.
    /// </summary>
    private async Task<BybitSpotOrder> PublishExisting(BybitBotContext context, string clientOrderId, CancellationToken cancellationToken)
    {
        BybitSpotOrder order = await Find(context, "/v5/order/realtime", clientOrderId, cancellationToken)
            ?? await Find(context, "/v5/order/history", clientOrderId, cancellationToken)
            ?? throw new BybitOrderUnresolvedException();

        if (!BybitClientOrderIds.TryParse(order.ClientOrderId, out BotId? owner, out long engineOrderId) || owner != context.BotId ||
            order.Symbol != context.Symbol)
        {
            throw new BybitOrderUnresolvedException();
        }

        updates.Publish(context.BotId, BybitOrderTranslation.Updates(engineOrderId, order, context.Instrument.BaseAsset, context.Instrument.QuoteAsset));

        return order;
    }

    private async Task<BybitSpotOrder?> Find(BybitBotContext context, string path, string clientOrderId, CancellationToken cancellationToken)
    {
        BybitRestResult found = await read.Get(context.Credential, path, $"category=spot&orderLinkId={Uri.EscapeDataString(clientOrderId)}", cancellationToken);

        if (found.Outcome != BybitRestOutcome.Accepted)
        {
            return found.Outcome == BybitRestOutcome.NotFound ? null : throw new BybitOrderUnresolvedException();
        }

        try
        {
            JsonElement list = found.Body.GetProperty("result").GetProperty("list");

            return list.GetArrayLength() == 0
                ? null
                : BybitSpotOrder.Parse(list[0]) is { } order && order.ClientOrderId == clientOrderId ? order : throw new BybitOrderUnresolvedException();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new BybitOrderUnresolvedException();
        }
    }

    private async Task<decimal> LastPrice(string symbol, CancellationToken cancellationToken)
    {
        BybitRestResult ticker = await read.Public($"/v5/market/tickers?category=spot&symbol={Uri.EscapeDataString(symbol)}", cancellationToken);

        try
        {
            return ticker.Outcome == BybitRestOutcome.Accepted &&
                decimal.Parse(ticker.Body.GetProperty("result").GetProperty("list")[0].GetProperty("lastPrice").GetString()!, CultureInfo.InvariantCulture) is > 0m and var price
                    ? price
                    : throw new BybitOrderUnresolvedException();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or FormatException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new BybitOrderUnresolvedException();
        }
    }

    private static RejectionReason Rejection(int? code, OrderSide side)
    {
        return code switch
        {
            110004 or 110007 or 110012 => side == OrderSide.Buy ? RejectionReason.InsufficientQuote : RejectionReason.InsufficientBase,
            110003 or 30208 or 30209 => RejectionReason.InvalidPrice,
            110017 => RejectionReason.ZeroQuantity,
            _ => RejectionReason.InvalidAmount
        };
    }
}
