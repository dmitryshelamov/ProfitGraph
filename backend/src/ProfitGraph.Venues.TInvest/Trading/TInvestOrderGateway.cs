using System.Runtime.CompilerServices;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Domain.Bots.OrderWaiting;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.TInvest.MarketData;
using ProfitGraph.Venues.TInvest.Transport;

using Tinkoff.InvestApi.V1;

using OrderType = ProfitGraph.Engine.Accounting.OrderType;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     T-Invest MOEX share execution for live bots (ADR 0023). Registered only by a Worker with the live role and trading
///     enabled. Each attempt of an engine order is recorded in the module's attempts table before the place request may
///     leave the process and is sent as a DAY limit or a market order in whole lots under its deterministic request ID, once
///     the trading status allows it and a limit price fits the price band; until then it waits locally (T12). An
///     attempt that may have reached T-Invest is never sent again: it is resolved only by <c>GetOrderState</c> with its
///     request ID. Only a status whose description is a broker code rejects; every other failure is unknown, so the intent
///     retries and eventually degrades the bot. Reconciliation checks the account's access, subscribes to the account's
///     order states and refuses another active order on the bot's share unless the bot allows them. An accepted order that
///     T-Invest no longer finds is closed when the owner confirmed it removed, or when the broker's own records prove it was
///     removed with nothing executed (ADR 0028, ADR 0030); otherwise it is unresolved. A DAY order the exchange ended with its
///     trading day ends with what it executed, read from T-Invest or from what the module recorded, while the account's
///     position covers its bots (ADR 0031).
/// </summary>
internal sealed class TInvestOrderGateway(
    ITInvestBotContexts contexts,
    ITInvestOrderAttempts attempts,
    TInvestOrderReads reads,
    TInvestOrderStreams streams,
    TInvestOrderUpdates updates,
    TInvestPrices prices,
    TInvestTradingStatuses statuses,
    TInvestPriceBands bands,
    TInvestSchedules schedules,
    ITInvestOrderWaits waits,
    TInvestOrderProgress progress,
    TInvestAbsenceProofs proofs,
    TInvestTransport transport,
    IOptions<TInvestOptions> options,
    TimeProvider clock) : IOrderGateway
{
    /// <summary>
    ///     The checks behind closing a DAY order the exchange ended with its trading day (ADR 0031), as the bot's feed lists them.
    /// </summary>
    private static readonly OrderAbsenceProofInternal ExpiryProof = new OrderAbsenceProofInternal(
        [TInvestAbsenceChecks.CancelledByExchange, TInvestAbsenceChecks.TradingDayEnded, TInvestAbsenceChecks.PositionCoversBots]);

    public VenueId Venue => TInvestVenue.VenueId;

    public ClientOrderId ClientOrderIdFor(BotId botId, long engineOrderId)
    {
        return TInvestClientOrderIds.For(botId, engineOrderId);
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

    /// <summary>
    ///     Places the order's first attempt, or sends an attempt recorded before it left the process. An attempt that may
    ///     have reached T-Invest is looked up instead, and a definitively rejected one is rejected again.
    /// </summary>
    public async Task<ExternalOrderUpdate?> Submit(OrderSubmitInternal submit, Func<CancellationToken, Task> markPlaceStarted, CancellationToken cancellationToken)
    {
        TInvestBotContext context = await contexts.Get(submit.BotId, cancellationToken);
        OrderSnapshot order = submit.Order;
        TInvestOrderAttempt? attempt = await attempts.Latest(submit.BotId.Value, order.Id, cancellationToken);

        if (attempt is null)
        {
            decimal? lastPrice = order is { Type: OrderType.Market, Side: OrderSide.Buy } ? await prices.Last(context.Token, context.Share, cancellationToken) : null;

            if (TInvestOrderPlans.For(order, context.Share, lastPrice, options.Value.MarketBuySlippage) is not { } plan)
            {
                return ExternalOrderUpdate.Reject(order.Id, RejectionReason.InvalidAmount, "The order is not a whole number of T-Invest lots on the price tick.");
            }

            DateTimeOffset now = clock.GetUtcNow();
            var planned = new TInvestOrderAttempt(
                submit.BotId.Value,
                order.Id,
                Attempt: 1,
                TInvestOrderRequestIds.For(submit.BotId, order.Id, attempt: 1),
                context.Share.Uid,
                order.Side,
                order.Type,
                plan.Lots,
                plan.Price,
                TInvestOrderAttemptState.Planned,
                ExchangeOrderId: null,
                BrokerCode: null,
                now,
                now);
            attempt = await attempts.Add(planned, cancellationToken)
                ? planned
                : await attempts.Latest(submit.BotId.Value, order.Id, cancellationToken) ?? throw new TInvestOrderUnresolvedException();
        }

        switch (attempt)
        {
            case { State: TInvestOrderAttemptState.Planned }:
                return await Release(context, attempt, markPlaceStarted, cancellationToken);
            case { State: TInvestOrderAttemptState.Final, ExchangeOrderId: null, BrokerCode: { } code }:
                return TInvestRejections.For(order.Id, code, order.Side);
            default:
                await PublishExisting(context, attempt, cancellationToken);

                return null;
        }
    }

    /// <summary>
    ///     Cancels the order's latest attempt by its request ID; the outcome is read back and published. An attempt that never
    ///     left the process is cancelled locally.
    /// </summary>
    public async Task Cancel(BotId botId, ClientOrderId clientOrderId, CancellationToken cancellationToken)
    {
        if (!TInvestClientOrderIds.TryParse(clientOrderId.Value, out BotId? owner, out long engineOrderId) || owner != botId)
        {
            return;
        }

        TInvestOrderAttempt? attempt = await attempts.Latest(botId.Value, engineOrderId, cancellationToken);

        if (attempt is null || attempt.State == TInvestOrderAttemptState.Planned)
        {
            // The attempt is recorded before any request, so an order without a sent attempt never reached T-Invest.
            if (attempt is not null &&
                !await attempts.Transition(
                    attempt with
                    {
                        State = TInvestOrderAttemptState.Final,
                        WaitingCode = null,
                        BandLow = null,
                        BandHigh = null,
                        CancelledBy = TInvestCancelReason.Owner,
                        UpdatedAt = clock.GetUtcNow()
                    },
                    attempt.State,
                    cancellationToken))
            {
                throw new TInvestOrderUnresolvedException();
            }

            updates.Publish(botId, [ExternalOrderUpdate.ConfirmCancel(engineOrderId)]);

            return;
        }

        if (attempt is { State: TInvestOrderAttemptState.Final, ExchangeOrderId: null })
        {
            return;
        }

        if (attempt.State != TInvestOrderAttemptState.Final && attempt.CancelledBy != TInvestCancelReason.Owner)
        {
            // An owner or strategy cancel wins over a carry-over in progress, and is recorded before the request leaves.
            TInvestOrderAttempt marked = attempt with { CancelledBy = TInvestCancelReason.Owner, UpdatedAt = clock.GetUtcNow() };
            attempt = await attempts.Transition(marked, attempt.State, cancellationToken) ? marked : throw new TInvestOrderUnresolvedException();
        }

        TInvestBotContext context = await contexts.Get(botId, cancellationToken);
        var requested = true;

        try
        {
            await transport.Unary(
                context.Token,
                TInvestMethodGroup.Orders,
                (invoker, callOptions) => new OrdersService.OrdersServiceClient(invoker).CancelOrderAsync(
                    new CancelOrderRequest { AccountId = context.AccountId, OrderId = attempt.RequestId.ToString("D"), OrderIdType = OrderIdType.Request },
                    callOptions),
                cancellationToken);
        }
        catch (TInvestRpcException)
        {
            requested = false;
        }

        OrderState state = await PublishExisting(context, attempt, cancellationToken);

        if (!requested && !TInvestOrderTranslation.IsFinal(state))
        {
            throw new TInvestOrderUnresolvedException();
        }
    }

    /// <summary>
    ///     Checks that the token still has full access to an open account, subscribes to its order states, then looks up every
    ///     order the bot asks about and the account's active orders on the share. An order that may have reached T-Invest but
    ///     is not found is unresolved; an active order on the share that is not the bot's is foreign unless the bot allows them.
    /// </summary>
    public async Task<BotReconciliationInternal> Reconcile(BotReconcileInternal request, CancellationToken cancellationToken)
    {
        TInvestBotContext context = await contexts.Get(request.BotId, cancellationToken);

        if (!await AccountMayTrade(context, cancellationToken))
        {
            return new BotReconciliationInternal([], [], BotStatusReason.KeyPermissions);
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
    ///     Whether reconciliation would now close, on the broker's own records, every order of the request that T-Invest no
    ///     longer finds (ADR 0030), and has no other reason to stop the bot. It reads and changes nothing: no stream is opened and
    ///     no attempt moves.
    /// </summary>
    public async Task<bool> CanProveAbsence(BotReconcileInternal request, CancellationToken cancellationToken)
    {
        TInvestBotContext context = await contexts.Get(request.BotId, cancellationToken);

        if (!await AccountMayTrade(context, cancellationToken))
        {
            return false;
        }

        OrderState[] onShare = [.. (await reads.Active(context, cancellationToken)).Where(order => order.InstrumentUid == context.Share.Uid)];
        var proven = false;

        foreach (ReconcileOrderInternal order in request.Orders)
        {
            TInvestOrderAttempt? attempt = await attempts.Latest(request.BotId.Value, order.EngineOrderId, cancellationToken);

            switch (attempt)
            {
                case null when order.MayHaveReachedVenue:
                    return false;
                case null or { State: TInvestOrderAttemptState.Planned } or { State: TInvestOrderAttemptState.Final, ExchangeOrderId: null, BrokerCode: not null }:
                    continue;
            }

            OrderState? known = await reads.ByRequestId(context, attempt.RequestId, cancellationToken);

            if (known is not null)
            {
                if (known.ExecutionReportStatus == OrderExecutionReportStatus.ExecutionReportStatusCancelled &&
                    attempt.CancelledBy is not (TInvestCancelReason.Owner or TInvestCancelReason.CarryOver))
                {
                    if (attempt.CancelledBy != TInvestCancelReason.Expired &&
                        (attempt.State == TInvestOrderAttemptState.Final || !await progress.Expired(context, attempt, cancellationToken)))
                    {
                        return false;
                    }

                    TInvestExecution total = (await progress.Executed(attempt.BotId, attempt.EngineOrderId, attempt.Attempt, cancellationToken))
                        .Add(TInvestOrderTranslation.Executed(known, context.Share.Lot));

                    if (await progress.Covers(context, attempt, total, order.ExecutedBase, cancellationToken) is not null)
                    {
                        return false;
                    }

                    proven = true;
                }

                continue;
            }

            if (attempt is { State: TInvestOrderAttemptState.Final, ExchangeOrderId: not null, Lots: > 0 } && attempt.ExecutedLots == attempt.Lots)
            {
                continue;
            }

            if (attempt is { State: TInvestOrderAttemptState.Final, ExchangeOrderId: not null, CancelledBy: TInvestCancelReason.Owner or TInvestCancelReason.Expired })
            {
                TInvestExecution total = await progress.Executed(attempt.BotId, attempt.EngineOrderId, attempt.Attempt + 1, cancellationToken);

                if (attempt.CancelledBy == TInvestCancelReason.Expired &&
                    await progress.Covers(context, attempt, total, order.ExecutedBase, cancellationToken) is not null)
                {
                    return false;
                }

                proven = true;

                continue;
            }

            if (attempt is not { State: TInvestOrderAttemptState.Active, ExchangeOrderId: { } exchangeOrderId } ||
                !(await proofs.Evaluate(context, attempt, exchangeOrderId, onShare, order, request.ExpectedBase, cancellationToken)).IsProven)
            {
                return false;
            }

            proven = true;
        }

        return proven;
    }

    /// <summary>
    ///     Sends a planned attempt when the trading status and the price band allow it, and otherwise keeps it waiting
    ///     locally: the engine sees a working order, the runtime the reason. Returns a definitive rejection, or null. The
    ///     module's worker calls it again for waiting attempts once the bot may send.
    /// </summary>
    internal async Task<ExternalOrderUpdate?> Release(
        TInvestBotContext context,
        TInvestOrderAttempt planned,
        Func<CancellationToken, Task> markPlaceStarted,
        CancellationToken cancellationToken)
    {
        TInvestSendDecision decision = await Decide(context, planned, cancellationToken);

        if (decision.WaitingCode is { } code)
        {
            await Wait(context, planned, code, decision.Band, cancellationToken);

            return null;
        }

        await markPlaceStarted(cancellationToken);
        TInvestOrderAttempt sent = planned with
        {
            State = TInvestOrderAttemptState.Sent,
            WaitingCode = null,
            BandLow = null,
            BandHigh = null,
            UpdatedAt = clock.GetUtcNow()
        };

        if (!await attempts.Transition(sent, TInvestOrderAttemptState.Planned, cancellationToken))
        {
            throw new TInvestOrderUnresolvedException();
        }

        PostOrderResponse response;

        try
        {
            response = await transport.Unary(
                context.Token,
                TInvestMethodGroup.Orders,
                (invoker, callOptions) => new OrdersService.OrdersServiceClient(invoker).PostOrderAsync(Request(context, sent, decision.Price), callOptions),
                cancellationToken);
        }
        catch (TInvestRpcException error) when (error is { Kind: TInvestFailureKind.Rejected, BrokerCode: { } brokerCode })
        {
            await attempts.Transition(
                sent with { State = TInvestOrderAttemptState.Final, BrokerCode = brokerCode, UpdatedAt = clock.GetUtcNow() },
                sent.State,
                cancellationToken);

            if (WaitingCodeOf(brokerCode) is not { } waiting)
            {
                return TInvestRejections.For(planned.EngineOrderId, brokerCode, planned.Side);
            }

            await Replan(context, sent, waiting, cancellationToken);

            return null;
        }
        catch (TInvestRpcException)
        {
            await attempts.Transition(sent with { State = TInvestOrderAttemptState.Unknown, UpdatedAt = clock.GetUtcNow() }, sent.State, cancellationToken);

            throw new TInvestOrderUnresolvedException();
        }

        TInvestOrderAttempt active = sent with { State = TInvestOrderAttemptState.Active, ExchangeOrderId = response.OrderId, UpdatedAt = clock.GetUtcNow() };
        await attempts.Transition(active, sent.State, cancellationToken);

        if (planned.WaitingCode is not null)
        {
            await waits.Report(context.BotId, planned.EngineOrderId, code: null, low: null, high: null, cancellationToken);
        }

        updates.Publish(context.BotId, [ExternalOrderUpdate.Accept(planned.EngineOrderId)]);

        if (response.LotsExecuted > 0 || response.ExecutionReportStatus != OrderExecutionReportStatus.ExecutionReportStatusNew)
        {
            await PublishExisting(context, active, cancellationToken);
        }

        return null;
    }

    /// <summary>
    ///     Only the current trading status authorizes a send, and a holiday interval of the schedule counts as closed; a limit
    ///     order must also fit the price band. A carried remainder leaves only on a later trading date than the one it was
    ///     carried from, which needs the schedule; it keeps its carry-over reason until then.
    /// </summary>
    private async Task<TInvestSendDecision> Decide(TInvestBotContext context, TInvestOrderAttempt attempt, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();
        IReadOnlyList<TInvestTradingDay>? days = await schedules.Days(context, cancellationToken);
        bool carried = attempt.WaitingCode == OrderWaitingCode.CarryOver;
        OrderWaitingCode closed = carried ? OrderWaitingCode.CarryOver : OrderWaitingCode.SessionClosed;

        if (carried && (days is null || TInvestSchedules.Current(days, now) is not { } day || day.Date <= attempt.CarriedFrom))
        {
            return TInvestSendDecision.Wait(OrderWaitingCode.CarryOver, band: null);
        }

        bool holiday = days?.Any(day => day.Sessions.Any(session => session.Holiday && session.From <= now && now < session.To)) == true;

        if (holiday || !await statuses.Allows(context, attempt.Type, cancellationToken))
        {
            return TInvestSendDecision.Wait(closed, band: null);
        }

        return attempt.Price is { } price
            ? TInvestSendDecisions.ForLimit(attempt.Side, price, await bands.Get(context, cancellationToken), context.Share.PriceIncrement)
            : TInvestSendDecision.Send(price: null);
    }

    /// <summary>
    ///     Cancels a working DAY limit attempt before its trading day ends, to carry its remainder to the next one (ADR 0023,
    ///     T12b; Profit Fox Designer ADR 0075). The carry-over is recorded before the cancel leaves; the engine order stays
    ///     working. A cancel that is not final yet is tried again by the next round.
    /// </summary>
    internal async Task CarryOver(TInvestBotContext context, TInvestOrderAttempt attempt, DateOnly tradingDay, CancellationToken cancellationToken)
    {
        if (attempt.CancelledBy is null)
        {
            TInvestOrderAttempt marked = attempt with { CancelledBy = TInvestCancelReason.CarryOver, CarriedFrom = tradingDay, UpdatedAt = clock.GetUtcNow() };

            if (!await attempts.Transition(marked, attempt.State, cancellationToken))
            {
                return;
            }

            attempt = marked;
        }

        try
        {
            await transport.Unary(
                context.Token,
                TInvestMethodGroup.Orders,
                (invoker, callOptions) => new OrdersService.OrdersServiceClient(invoker).CancelOrderAsync(
                    new CancelOrderRequest { AccountId = context.AccountId, OrderId = attempt.RequestId.ToString("D"), OrderIdType = OrderIdType.Request },
                    callOptions),
                cancellationToken);
        }
        catch (TInvestRpcException)
        {
            // The final state read below decides; a filled order needs no cancel.
        }

        await PublishExisting(context, attempt, cancellationToken);
    }

    /// <summary>
    ///     Keeps the attempt waiting with its reason. The engine hears a local acceptance when the attempt starts to wait; the
    ///     runtime hears the reason whenever it or the band changed.
    /// </summary>
    private async Task Wait(TInvestBotContext context, TInvestOrderAttempt attempt, OrderWaitingCode code, TInvestPriceBand? band, CancellationToken cancellationToken)
    {
        TInvestOrderAttempt waiting = attempt with { WaitingCode = code, BandLow = band?.Low, BandHigh = band?.High, UpdatedAt = clock.GetUtcNow() };

        if (attempt.WaitingCode == code && attempt.BandLow == band?.Low && attempt.BandHigh == band?.High)
        {
            return;
        }

        if (!await attempts.Transition(waiting, TInvestOrderAttemptState.Planned, cancellationToken))
        {
            throw new TInvestOrderUnresolvedException();
        }

        if (attempt.WaitingCode is null)
        {
            updates.Publish(context.BotId, [ExternalOrderUpdate.AcceptLocally(attempt.EngineOrderId)]);
        }

        await waits.Report(context.BotId, attempt.EngineOrderId, code, band?.Low, band?.High, cancellationToken);
    }

    /// <summary>
    ///     A definitive <c>30098</c> or <c>30099</c> placed no order: the same engine order waits again as the next attempt,
    ///     with the next request ID, and the engine hears no rejection.
    /// </summary>
    private async Task Replan(TInvestBotContext context, TInvestOrderAttempt refused, OrderWaitingCode code, CancellationToken cancellationToken)
    {
        int number = refused.Attempt + 1;
        DateTimeOffset now = clock.GetUtcNow();
        TInvestOrderAttempt next = refused with
        {
            Attempt = number,
            RequestId = TInvestOrderRequestIds.For(context.BotId, refused.EngineOrderId, number),
            State = TInvestOrderAttemptState.Planned,
            ExchangeOrderId = null,
            BrokerCode = null,
            CreatedAt = now,
            UpdatedAt = now,
            WaitingCode = null,
            BandLow = null,
            BandHigh = null
        };

        if (!await attempts.Add(next, cancellationToken))
        {
            throw new TInvestOrderUnresolvedException();
        }

        await Wait(context, next, code, band: null, cancellationToken);
    }

    private async Task<BotReconciliationInternal> LookUp(TInvestBotContext context, BotReconcileInternal request, CancellationToken cancellationToken)
    {
        OrderState[] onShare = [.. (await reads.Active(context, cancellationToken)).Where(order => order.InstrumentUid == context.Share.Uid)];
        Guid[] requestIds = [.. onShare.Select(order => Guid.TryParse(order.OrderRequestId, out Guid id) ? id : Guid.Empty).Where(id => id != Guid.Empty)];
        IReadOnlyDictionary<Guid, Guid> owners = await attempts.Owners(requestIds, cancellationToken);
        List<ExternalOrderUpdate> found = [];
        List<ReconciledOrderInternal> orders = [];

        foreach (ReconcileOrderInternal order in request.Orders)
        {
            TInvestOrderAttempt? attempt = await attempts.Latest(request.BotId.Value, order.EngineOrderId, cancellationToken);

            switch (attempt)
            {
                case null when order.MayHaveReachedVenue:
                    return new BotReconciliationInternal(found, orders, BotStatusReason.UnresolvedOrder);
                case { State: TInvestOrderAttemptState.Planned, WaitingCode: { } waiting }:
                    found.AddRange(await CarriedExecutions(context, attempt, cancellationToken));
                    found.Add(ExternalOrderUpdate.AcceptLocally(order.EngineOrderId));
                    orders.Add(new ReconciledOrderInternal(order.ClientOrderId, IsFinal: false));
                    await waits.Report(request.BotId, order.EngineOrderId, waiting, attempt.BandLow, attempt.BandHigh, cancellationToken);

                    continue;
                case null or { State: TInvestOrderAttemptState.Planned }:
                    continue;
                case { State: TInvestOrderAttemptState.Final, ExchangeOrderId: null, BrokerCode: { } code }:
                    found.Add(TInvestRejections.For(order.EngineOrderId, code, attempt.Side));
                    orders.Add(new ReconciledOrderInternal(order.ClientOrderId, IsFinal: true));

                    continue;
            }

            OrderState? known = await reads.ByRequestId(context, attempt.RequestId, cancellationToken);

            if (known is null && attempt is { State: TInvestOrderAttemptState.Final, ExchangeOrderId: not null, Lots: > 0 } && attempt.ExecutedLots == attempt.Lots)
            {
                // The module recorded the fill while no runner read it, such as at a carry-over after the bot failed (ADR 0030), and T-Invest no
                // longer answers for a past day's order: the recorded totals are the final state.
                found.AddRange(await RecordedFill(context, attempt, cancellationToken));
                orders.Add(new ReconciledOrderInternal(order.ClientOrderId, IsFinal: true));

                continue;
            }

            if (known is null && attempt is
                    { State: TInvestOrderAttemptState.Final, ExchangeOrderId: not null, CancelledBy: TInvestCancelReason.Owner or TInvestCancelReason.Expired })
            {
                // The module recorded how the order ended while no runner read it, such as by the post-session sweep (ADR 0031), and
                // T-Invest no longer answers for a past day's order.
                try
                {
                    found.AddRange(await progress.Recorded(context, attempt, order.ExecutedBase, cancellationToken));
                }
                catch (OrderGatewaySafetyException safety)
                {
                    return new BotReconciliationInternal(found, orders, safety.Reason, safety.Detail);
                }

                orders.Add(new ReconciledOrderInternal(order.ClientOrderId, IsFinal: true, attempt.CancelledBy == TInvestCancelReason.Expired ? ExpiryProof : null));

                continue;
            }

            if (known is null && order.OwnerConfirmedAbsent && attempt is { State: TInvestOrderAttemptState.Final, ExchangeOrderId: not null, CancelledBy: null } &&
                await ConfirmRecordedCancel(attempt, cancellationToken))
            {
                found.Add(ExternalOrderUpdate.ConfirmCancel(order.EngineOrderId));
                orders.Add(new ReconciledOrderInternal(order.ClientOrderId, IsFinal: true));

                continue;
            }

            string? failedCheck = null;

            if (known is null && attempt is { State: TInvestOrderAttemptState.Active, ExchangeOrderId: { } exchangeOrderId })
            {
                if (order.OwnerConfirmedAbsent)
                {
                    if (await ProvenRemoved(context, attempt, exchangeOrderId, onShare, cancellationToken))
                    {
                        found.Add(ExternalOrderUpdate.ConfirmCancel(order.EngineOrderId));
                        orders.Add(new ReconciledOrderInternal(order.ClientOrderId, IsFinal: true));

                        continue;
                    }
                }
                else
                {
                    TInvestAbsenceProof evidence = await proofs.Evaluate(context, attempt, exchangeOrderId, onShare, order, request.ExpectedBase, cancellationToken);

                    if (evidence.IsProven && await CloseRemoved(attempt, cancellationToken))
                    {
                        found.Add(ExternalOrderUpdate.ConfirmCancel(order.EngineOrderId));
                        orders.Add(new ReconciledOrderInternal(order.ClientOrderId, IsFinal: true, evidence.ToInternal()));

                        continue;
                    }

                    failedCheck = evidence.Failed ?? TInvestAbsenceChecks.AttemptChanged;
                }
            }

            if (known is not { } state)
            {
                return new BotReconciliationInternal(found, orders, BotStatusReason.UnresolvedOrder, failedCheck is null ? null : $"AutoCheck:{failedCheck}");
            }

            try
            {
                found.AddRange(await progress.Apply(context, attempt, state, cancellationToken, order.ExecutedBase));
            }
            catch (OrderGatewaySafetyException safety)
            {
                return new BotReconciliationInternal(found, orders, safety.Reason, safety.Detail);
            }

            bool expired = state.ExecutionReportStatus == OrderExecutionReportStatus.ExecutionReportStatusCancelled &&
                (await attempts.ByRequestId(attempt.RequestId, cancellationToken))?.CancelledBy == TInvestCancelReason.Expired;
            orders.Add(new ReconciledOrderInternal(order.ClientOrderId, TInvestOrderTranslation.IsFinal(state), expired ? ExpiryProof : null));
        }

        bool foreign = !request.AllowForeignOrders &&
            onShare.Any(order => !Guid.TryParse(order.OrderRequestId, out Guid id) || !owners.TryGetValue(id, out Guid owner) || owner != request.BotId.Value);

        return new BotReconciliationInternal(found, orders, foreign ? BotStatusReason.ForeignOrder : null);
    }

    /// <summary>
    ///     Whether an attempt T-Invest had accepted, which the owner confirmed removed (ADR 0028), left nothing behind: no
    ///     order of the account on the share carries its request ID or exchange order ID, T-Invest no longer knows it by the
    ///     exchange order ID either, and no attempt of the engine order executed anything. When it ends so, the attempt is
    ///     closed as an owner cancel; anything T-Invest still shows, or a read that fails, leaves the order unresolved.
    /// </summary>
    private async Task<bool> ProvenRemoved(
        TInvestBotContext context,
        TInvestOrderAttempt attempt,
        string exchangeOrderId,
        IReadOnlyList<OrderState> onShare,
        CancellationToken cancellationToken)
    {
        if (onShare.Any(active => active.OrderId == exchangeOrderId || active.OrderRequestId == attempt.RequestId.ToString("D")) ||
            (await attempts.ForOrder(attempt.BotId, attempt.EngineOrderId, cancellationToken)).Any(other => other.ExecutedLots > 0) ||
            await reads.ByExchangeOrderId(context, exchangeOrderId, cancellationToken) is not null)
        {
            return false;
        }

        return await CloseRemoved(attempt, cancellationToken);
    }

    /// <summary>
    ///     An attempt recorded as cancelled although nobody asked for it, which T-Invest no longer answers for, ends as the owner
    ///     confirmed (ADR 0028) when no attempt of its engine order executed anything; the recorded final state stands in for the
    ///     lookups the owner's confirmation otherwise needs. The attempt is marked as the owner's cancel.
    /// </summary>
    private async Task<bool> ConfirmRecordedCancel(TInvestOrderAttempt attempt, CancellationToken cancellationToken)
    {
        if ((await attempts.ForOrder(attempt.BotId, attempt.EngineOrderId, cancellationToken)).Any(other => other.ExecutedLots > 0))
        {
            return false;
        }

        return await attempts.Transition(
            attempt with { CancelledBy = TInvestCancelReason.Owner, UpdatedAt = clock.GetUtcNow() },
            TInvestOrderAttemptState.Final,
            cancellationToken);
    }

    /// <summary>
    ///     Closes an attempt T-Invest no longer knows as an owner cancel with nothing executed, so the engine cancels the order
    ///     as it does for the owner's. Moves the attempt only from <see cref="TInvestOrderAttemptState.Active" />.
    /// </summary>
    private async Task<bool> CloseRemoved(TInvestOrderAttempt attempt, CancellationToken cancellationToken)
    {
        TInvestOrderAttempt closed = attempt with
        {
            State = TInvestOrderAttemptState.Final,
            CancelledBy = TInvestCancelReason.Owner,
            UpdatedAt = clock.GetUtcNow()
        };

        return await attempts.Transition(closed, attempt.State, cancellationToken);
    }

    /// <summary>
    ///     What earlier attempts of the engine order executed, as the cumulative updates the engine needs when it did not hear
    ///     them: an engine order whose first attempt was carried over while no runner read its updates continues as a later
    ///     attempt that waits. The engine ignores a total it already booked.
    /// </summary>
    private async Task<IReadOnlyList<ExternalOrderUpdate>> CarriedExecutions(TInvestBotContext context, TInvestOrderAttempt attempt, CancellationToken cancellationToken)
    {
        TInvestExecution earlier = await Executed(attempt.BotId, attempt.EngineOrderId, attempt.Attempt, cancellationToken);

        return earlier.Lots == 0
            ? []
            :
            [
                ExternalOrderUpdate.Accept(attempt.EngineOrderId),
                ExternalOrderUpdate.Partial(
                    attempt.EngineOrderId,
                    earlier.Lots * context.Share.Lot,
                    earlier.Quote,
                    earlier.Fee,
                    earlier.Fee == 0m,
                    commissionPartitioned: true)
            ];
    }

    /// <summary>
    ///     The final fill of an engine order from the module's records: the totals of every attempt up to and including this one.
    /// </summary>
    private async Task<IReadOnlyList<ExternalOrderUpdate>> RecordedFill(TInvestBotContext context, TInvestOrderAttempt attempt, CancellationToken cancellationToken)
    {
        TInvestExecution total = await Executed(attempt.BotId, attempt.EngineOrderId, attempt.Attempt + 1, cancellationToken);

        return
        [
            ExternalOrderUpdate.Accept(attempt.EngineOrderId),
            ExternalOrderUpdate.Final(
                attempt.EngineOrderId,
                total.Lots * context.Share.Lot,
                total.Quote,
                total.Fee,
                attempt.ExecutedFee == 0m,
                commissionPartitioned: true)
        ];
    }

    private async Task<TInvestExecution> Executed(Guid botId, long engineOrderId, int before, CancellationToken cancellationToken)
    {
        return (await attempts.ForOrder(botId, engineOrderId, cancellationToken))
            .Where(other => other.Attempt < before && other.State == TInvestOrderAttemptState.Final)
            .Aggregate(TInvestExecution.None, (sum, other) => sum.Add(new TInvestExecution(other.ExecutedLots, other.ExecutedQuote, other.ExecutedFee)));
    }

    /// <summary>
    ///     Reads the attempt's state by its request ID and publishes it. An empty answer is not proof that the order was never
    ///     placed.
    /// </summary>
    private async Task<OrderState> PublishExisting(TInvestBotContext context, TInvestOrderAttempt attempt, CancellationToken cancellationToken)
    {
        OrderState state = await reads.ByRequestId(context, attempt.RequestId, cancellationToken) ?? throw new TInvestOrderUnresolvedException();
        updates.Publish(context.BotId, await progress.Apply(context, attempt, state, cancellationToken));

        return state;
    }

    private async Task<bool> AccountMayTrade(TInvestBotContext context, CancellationToken cancellationToken)
    {
        GetAccountsResponse response;

        try
        {
            response = await transport.Unary(
                context.Token,
                TInvestMethodGroup.Users,
                (invoker, callOptions) => new UsersService.UsersServiceClient(invoker).GetAccountsAsync(new GetAccountsRequest(), callOptions),
                cancellationToken);
        }
        catch (TInvestRpcException error) when (error.Kind is TInvestFailureKind.Unauthenticated or TInvestFailureKind.PermissionDenied)
        {
            return false;
        }

        return response.Accounts.Any(account => account.Id == context.AccountId && account.Status == AccountStatus.Open &&
            account.AccessLevel == AccessLevel.AccountAccessLevelFullAccess);
    }

    private static OrderWaitingCode? WaitingCodeOf(int brokerCode)
    {
        return brokerCode switch
        {
            30098 => OrderWaitingCode.SessionClosed,
            30099 => OrderWaitingCode.PriceBand,
            _ => null
        };
    }

    private static PostOrderRequest Request(TInvestBotContext context, TInvestOrderAttempt attempt, decimal? price)
    {
        var request = new PostOrderRequest
        {
            AccountId = context.AccountId,
            InstrumentId = attempt.InstrumentUid,
            OrderId = attempt.RequestId.ToString("D"),
            Quantity = attempt.Lots,
            Direction = attempt.Side == OrderSide.Buy ? OrderDirection.Buy : OrderDirection.Sell,
            OrderType = attempt.Type == OrderType.Limit ? Tinkoff.InvestApi.V1.OrderType.Limit : Tinkoff.InvestApi.V1.OrderType.Market
        };

        if (price is { } limit)
        {
            request.Price = limit.ToQuotation();
            request.TimeInForce = TimeInForceType.TimeInForceDay;
        }

        return request;
    }
}