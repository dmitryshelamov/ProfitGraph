using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Bybit Spot order execution (ADR 0017, Profit Fox Designer ADR 0055). Registered only by a Worker with the live role
///     and trading enabled. A first attempt places; a later attempt, or a duplicate response, looks the order up by its
///     client ID and never places again on an empty answer. Only an explicit Bybit rejection rejects; every unknown
///     outcome is an exception, so the intent retries and eventually degrades the bot. Reconciliation checks the key and
///     the symbol before a bot trades (ADR 0016, stage 5.8). A submit Bybit does not know is closed only when a long wait and
///     independent evidence prove it never took effect (ADR 0030); otherwise it is unresolved. The same gateway serves USDT
///     perpetuals (ADR 0061): reconciliation also requires the symbol in hedge mode, and an entry first sets the bot's leverage.
/// </summary>
internal sealed class BybitOrderGateway(
    BybitTradeClient trade,
    BybitReadClient read,
    BybitOrderReads reads,
    BybitPrivateStreams streams,
    BybitBotContexts contexts,
    BybitOrderUpdates updates,
    BybitAbsenceProofs proofs,
    BybitMarket? market = null) : IOrderGateway
{
    /// <summary>
    ///     Bybit's answer when the leverage asked for is the one already set.
    /// </summary>
    private const int LeverageNotModified = 110043;

    private readonly BybitMarket _market = market ?? BybitMarket.Spot;

    public VenueId Venue => _market.Venue;

    public ClientOrderId ClientOrderIdFor(BotId botId, long engineOrderId)
    {
        return BybitClientOrderIds.For(botId, engineOrderId);
    }

    /// <summary>
    ///     The updates of the stream reconciliation opened; the stream closes when the runner stops reading.
    /// </summary>
    public async IAsyncEnumerable<ExternalOrderUpdate> Updates(BotId botId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ExternalOrderUpdate update in updates.Read(botId, cancellationToken))
            {
                yield return update;
            }
        }
        finally
        {
            await streams.Close(botId);
        }
    }

    public async Task<ExternalOrderUpdate?> Submit(OrderSubmitInternal submit, Func<CancellationToken, Task> markPlaceStarted, CancellationToken cancellationToken)
    {
        BybitBotContext context = await contexts.Get(submit.BotId, cancellationToken);

        if (submit.MustLookUpBeforeSubmit)
        {
            await PublishExisting(context, submit.ClientOrderId.Value, cancellationToken);

            return null;
        }

        decimal? lastPrice = submit.Order.Type == OrderType.Market && (submit.Order.Side == OrderSide.Sell || context.Market.IsLinear)
            ? await LastPrice(context, cancellationToken)
            : null;
        string? body = BybitOrderRequests.Create(context, submit.ClientOrderId.Value, submit.Order, lastPrice);

        if (body is null)
        {
            return ExternalOrderUpdate.Reject(submit.Order.Id, RejectionReason.BelowMinNotional, $"The order is below Bybit {context.Market.Title} minimums.");
        }

        if (context.Market.IsLinear && !context.Reduces(submit.Order.Side))
        {
            await SetLeverage(context, cancellationToken);
        }

        BybitRestResult placed = await trade.Post(context.Credential, "/v5/order/create", body, markPlaceStarted, cancellationToken);

        switch (placed.Outcome)
        {
            case BybitRestOutcome.Accepted:
                return null;
            case BybitRestOutcome.Rejected:
                return ExternalOrderUpdate.Reject(
                    submit.Order.Id,
                    Rejection(placed.Code, submit.Order.Side, context),
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
        string body = JsonSerializer.Serialize(new { category = context.Market.Name, symbol = context.Symbol, orderLinkId = clientOrderId.Value });
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
    ///     Checks the key, opens the bot's private stream, then looks up every order the bot asks about and what else
    ///     trades on its symbol (Profit Fox Designer ADR 0055). An order that may have reached Bybit but is not found is
    ///     unresolved; an order or execution on the symbol that is not the bot's is foreign unless the bot allows them.
    /// </summary>
    public async Task<BotReconciliationInternal> Reconcile(BotReconcileInternal request, CancellationToken cancellationToken)
    {
        BybitBotContext context = await contexts.Get(request.BotId, cancellationToken);

        if (!await reads.KeyMayTrade(context, cancellationToken))
        {
            return new BotReconciliationInternal([], [], BotStatusReason.KeyPermissions);
        }

        // Long and short trades of one pair hold separate sides only in hedge mode, which the owner sets (ADR 0061).
        if (context.Market.IsLinear && await reads.HedgePosition(context, cancellationToken) is null)
        {
            return new BotReconciliationInternal([], [], BotStatusReason.VenuePositionMode);
        }

        await streams.Open(context, request.AllowForeignOrders, cancellationToken);

        try
        {
            BotReconciliationInternal reconciliation = await LookUp(context, request, cancellationToken);

            if (reconciliation.Degradation is not null)
            {
                await streams.Close(request.BotId);
            }

            return reconciliation;
        }
        catch
        {
            await streams.Close(request.BotId);

            throw;
        }
    }

    /// <summary>
    ///     Whether reconciliation would now close, on Bybit's own records, every order of the request that Bybit does not know and
    ///     has no other reason to stop the bot (ADR 0030). It only reads: no stream is opened.
    /// </summary>
    public async Task<bool> CanProveAbsence(BotReconcileInternal request, CancellationToken cancellationToken)
    {
        BybitBotContext context = await contexts.Get(request.BotId, cancellationToken);

        if (!await reads.KeyMayTrade(context, cancellationToken))
        {
            return false;
        }

        var proven = false;

        foreach (ReconcileOrderInternal order in request.Orders)
        {
            if (await reads.LookUp(context, order.ClientOrderId.Value, cancellationToken) is not null || !order.MayHaveReachedVenue)
            {
                continue;
            }

            if (!(await proofs.Evaluate(context, order, request.ExpectedBase, cancellationToken)).IsProven)
            {
                return false;
            }

            proven = true;
        }

        return proven;
    }

    private async Task<BotReconciliationInternal> LookUp(BybitBotContext context, BotReconcileInternal request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> onSymbol = await reads.SymbolClientOrderIds(context, cancellationToken);
        List<ExternalOrderUpdate> found = [];
        List<ReconciledOrderInternal> orders = [];

        foreach (ReconcileOrderInternal order in request.Orders)
        {
            BybitSpotOrder? existing = await reads.LookUp(context, order.ClientOrderId.Value, cancellationToken);

            if (existing is null)
            {
                if (!order.MayHaveReachedVenue)
                {
                    continue;
                }

                BybitAbsenceProof evidence = await proofs.Evaluate(context, order, request.ExpectedBase, cancellationToken);

                if (!evidence.IsProven)
                {
                    return new BotReconciliationInternal(found, orders, BotStatusReason.UnresolvedOrder, $"AutoCheck:{evidence.Failed}");
                }

                found.Add(
                    ExternalOrderUpdate.Reject(
                        order.EngineOrderId,
                        RejectionReason.AbsenceProven,
                        "Bybit shows nothing of the order after the settle delay.",
                        ownerConfirmedAbsent: true));
                orders.Add(new ReconciledOrderInternal(order.ClientOrderId, IsFinal: true, evidence.ToInternal()));

                continue;
            }

            found.AddRange(BybitOrderTranslation.Updates(order.EngineOrderId, existing, context));
            orders.Add(new ReconciledOrderInternal(order.ClientOrderId, existing.IsFinal));
        }

        bool foreign = !request.AllowForeignOrders &&
            onSymbol.Any(clientOrderId => !BybitClientOrderIds.TryParse(clientOrderId, out BotId? owner, out _) || owner != request.BotId);

        return new BotReconciliationInternal(found, orders, foreign ? BotStatusReason.ForeignOrder : null);
    }

    /// <summary>
    ///     Finds the order by its client ID and publishes its state. An empty answer is not proof that the order was never
    ///     placed.
    /// </summary>
    private async Task<BybitSpotOrder> PublishExisting(BybitBotContext context, string clientOrderId, CancellationToken cancellationToken)
    {
        BybitSpotOrder order = await reads.LookUp(context, clientOrderId, cancellationToken) ?? throw new BybitOrderUnresolvedException();
        BybitClientOrderIds.TryParse(order.ClientOrderId, out _, out long engineOrderId);
        updates.Publish(context.BotId, BybitOrderTranslation.Updates(engineOrderId, order, context));

        return order;
    }

    /// <summary>
    ///     Sets the bot's leverage on both sides of its USDT perpetual (ADR 0061); the leverage already set is success. Any other
    ///     answer leaves the entry unsent, so the intent retries and Bybit's refusal of a leverage above the contract's maximum
    ///     degrades the bot.
    /// </summary>
    private async Task SetLeverage(BybitBotContext context, CancellationToken cancellationToken)
    {
        string leverage = context.Leverage.ToString(CultureInfo.InvariantCulture);
        string body = JsonSerializer.Serialize(new { category = "linear", symbol = context.Symbol, buyLeverage = leverage, sellLeverage = leverage });
        BybitRestResult set = await trade.Post(context.Credential, "/v5/position/set-leverage", body, beforeSend: null, cancellationToken);

        if (set.Outcome != BybitRestOutcome.Accepted && set.Code != LeverageNotModified)
        {
            throw new BybitOrderUnresolvedException();
        }
    }

    private async Task<decimal> LastPrice(BybitBotContext context, CancellationToken cancellationToken)
    {
        BybitRestResult ticker = await read.Public(
            $"/v5/market/tickers?category={context.Market.Name}&symbol={Uri.EscapeDataString(context.Symbol)}",
            cancellationToken);

        try
        {
            return ticker.Outcome == BybitRestOutcome.Accepted &&
                decimal.Parse(
                    ticker.Body.GetProperty("result").GetProperty("list")[0].GetProperty("lastPrice").GetString()!,
                    CultureInfo.InvariantCulture) is > 0m and var price
                    ? price
                    : throw new BybitOrderUnresolvedException();
        }
        catch (Exception exception) when (exception is KeyNotFoundException or FormatException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new BybitOrderUnresolvedException();
        }
    }

    private static RejectionReason Rejection(int? code, OrderSide side, BybitBotContext context)
    {
        // A USDT perpetual's exit closes a position and its entry spends margin; 110017 there is a reduce-only order beyond the side.
        if (context.Market.IsLinear && code is 110004 or 110007 or 110012 or 110017)
        {
            return context.Reduces(side) ? RejectionReason.InsufficientBase : RejectionReason.InsufficientQuote;
        }

        return code switch
        {
            110004 or 110007 or 110012 => side == OrderSide.Buy ? RejectionReason.InsufficientQuote : RejectionReason.InsufficientBase,
            110003 or 30208 or 30209 => RejectionReason.InvalidPrice,
            110017 => RejectionReason.ZeroQuantity,
            _ => RejectionReason.InvalidAmount
        };
    }
}