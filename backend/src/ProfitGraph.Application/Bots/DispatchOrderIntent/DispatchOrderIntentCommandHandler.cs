using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.DispatchOrderIntent;

/// <summary>
///     Claims a due intent, delivers it through the venue's gateway and records the outcome (Profit Fox Designer ADRs
///     0054 and 0055). Before a submit may leave the process its place start is recorded; a submit that might have reached
///     the venue is looked up before any retry. A definitive rejection is stored, then handed to the bot's runner. A
///     failure retries with backoff, and the tenth fails the intent and degrades the bot. A safety reason from the venue
///     degrades the bot at once and returns the intent for a later lookup. A first buy above the bot's per-order limit
///     (a sell is bounded by the position the engine reserved, not by the limit) is rejected with <c>RiskLimit</c> before it is sent; a rejection for an insufficient balance degrades
///     the bot with
///     <c>InsufficientBalance</c>. Stopped bots hold their submits.
/// </summary>
internal sealed class DispatchOrderIntentCommandHandler(
    IOrderIntentRepository intents,
    IBotRepository bots,
    IBotLeaseRepository leases,
    ITradingHaltRepository halts,
    VenuePorts<IOrderGateway> gateways,
    BotUpdateBus updates,
    ITransactionExecutor transactions,
    TimeProvider clock) : IRequestHandler<DispatchOrderIntentCommand, bool>
{
    private const int CandidateLimit = 20;

    public async Task<bool> Handle(DispatchOrderIntentCommand command, CancellationToken cancellationToken)
    {
        var holder = new LeaseHolderId(command.HolderId);
        await HoldStoppedSubmits(cancellationToken);
        ClaimedOrderIntent? claimed = await Claim(holder, cancellationToken);

        if (claimed is null)
        {
            return false;
        }

        OrderIntent intent = claimed.Intent;
        IOrderGateway? gateway = gateways.Find(claimed.Venue);

        if (gateway is null)
        {
            await Release(intent, cancellationToken);

            return true;
        }

        try
        {
            if (intent.Kind == OrderIntentKind.Submit)
            {
                var submit = new OrderSubmitInternal(intent.BotId, intent.ClientOrderId, intent.Order!, intent.MustLookUpBeforeSubmit);

                if (!submit.MustLookUpBeforeSubmit && PerOrderLimit.Exceeds(intent.Order!, claimed.MaxOrderQuote))
                {
                    await Reject(
                        intent,
                        ExternalOrderUpdate.Reject(intent.EngineOrderId, RejectionReason.RiskLimit, "The order exceeds the bot's per-order limit."),
                        cancellationToken);

                    return true;
                }

                ExternalOrderUpdate? rejection = await gateway.Submit(submit, token => MarkPlaceStarted(intent, holder, claimed.Epoch!.Value, token), cancellationToken);

                if (rejection is not null)
                {
                    await Reject(intent, rejection, cancellationToken);

                    return true;
                }
            }
            else
            {
                await gateway.Cancel(intent.BotId, intent.ClientOrderId, cancellationToken);
            }

            await Change(intent.Id, stored => stored.MarkSent(clock.GetUtcNow()), cancellationToken);
        }
        catch (OrderSubmitBlockedException)
        {
            await Release(intent, CancellationToken.None);
        }
        catch (OrderGatewaySafetyException exception)
        {
            await Degrade(intent.BotId, exception.Reason, CancellationToken.None);
            await Release(intent, CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await Release(intent, CancellationToken.None);

            throw;
        }
        catch (Exception exception) when (exception is not (OrderSubmitBlockedException or OrderGatewaySafetyException))
        {
            await Fail(intent, CancellationToken.None);
        }

        return true;
    }

    private Task HoldStoppedSubmits(CancellationToken cancellationToken)
    {
        return transactions.ExecuteAsync(
            async () =>
            {
                foreach (BotId id in await intents.ListStoppedBotsWithWaitingSubmits(cancellationToken))
                {
                    Bot? bot = await bots.GetForUpdate(id, cancellationToken);

                    if (bot is not null && bot.HoldSubmits(clock.GetUtcNow()))
                    {
                        await bots.Update(bot, cancellationToken);
                    }
                }

                return true;
            },
            cancellationToken);
    }

    private Task<ClaimedOrderIntent?> Claim(LeaseHolderId holder, CancellationToken cancellationToken)
    {
        return transactions.ExecuteAsync(
            async () =>
            {
                DateTimeOffset now = clock.GetUtcNow();

                foreach (OrderIntentId id in await intents.ListDeliveryCandidates(holder, gateways.Venues, now, CandidateLimit, cancellationToken))
                {
                    // Rows are locked lease, bot, intent everywhere; the intent is read unlocked first only to find its bot.
                    if (await intents.Get(id, cancellationToken) is not { } candidate || !candidate.CanClaim(now))
                    {
                        continue;
                    }

                    BotLease? lease = await leases.GetForUpdate(candidate.BotId, cancellationToken);
                    Bot? bot = await bots.GetForUpdate(candidate.BotId, cancellationToken);
                    OrderIntent? intent = await intents.GetForUpdate(id, cancellationToken);

                    if (bot is null || intent is null || !intent.CanClaim(now))
                    {
                        continue;
                    }

                    long? epoch = null;

                    if (intent.Kind == OrderIntentKind.Submit)
                    {
                        TradingHalt halt = await halts.Get(cancellationToken);

                        if (!CanPlace(bot, lease, holder, lease?.Epoch ?? 0, halt, now))
                        {
                            continue;
                        }

                        epoch = lease!.Epoch;
                    }

                    intent.Claim(now);
                    await intents.Update(intent, cancellationToken);

                    return new ClaimedOrderIntent(intent, bot.Series.Venue, bot.RiskLimits.MaxOrderQuote, epoch);
                }

                return null;
            },
            cancellationToken);
    }

    private Task MarkPlaceStarted(OrderIntent claimed, LeaseHolderId holder, long epoch, CancellationToken cancellationToken)
    {
        return transactions.ExecuteAsync(
            async () =>
            {
                DateTimeOffset now = clock.GetUtcNow();
                BotLease? lease = await leases.GetForUpdate(claimed.BotId, cancellationToken);
                Bot? bot = await bots.GetForUpdate(claimed.BotId, cancellationToken);
                TradingHalt halt = await halts.Get(cancellationToken);
                OrderIntent? intent = await intents.GetForUpdate(claimed.Id, cancellationToken);

                if (bot is null || intent is not { Status: OrderIntentStatus.Sending } || !CanPlace(bot, lease, holder, epoch, halt, now))
                {
                    throw new OrderSubmitBlockedException();
                }

                intent.MarkPlaceStarted(now);
                await intents.Update(intent, cancellationToken);

                return true;
            },
            cancellationToken);
    }

    private async Task Reject(OrderIntent claimed, ExternalOrderUpdate rejection, CancellationToken cancellationToken)
    {
        var stored = new OrderIntentRejection(rejection.Reason ?? RejectionReason.BrokerRejected, rejection.Message);
        await Change(claimed.Id, intent => intent.Reject(stored, clock.GetUtcNow()), cancellationToken);
        updates.Publish(claimed.BotId, ExternalOrderUpdate.Reject(claimed.EngineOrderId, stored.Reason, stored.Message));

        if (stored.Reason is RejectionReason.InsufficientQuote or RejectionReason.InsufficientBase)
        {
            await Degrade(claimed.BotId, BotStatusReason.InsufficientBalance, cancellationToken);
        }
    }

    /// <summary>
    ///     What a submit commits in quote: a market buy its budget, a limit buy its price times quantity, a sell its
    ///     quantity at its limit or at the last closed candle (Profit Fox Designer ADR 0056).
    /// </summary>
    private Task Fail(OrderIntent claimed, CancellationToken cancellationToken)
    {
        return transactions.ExecuteAsync(
            async () =>
            {
                DateTimeOffset now = clock.GetUtcNow();
                Bot? bot = await bots.GetForUpdate(claimed.BotId, cancellationToken);
                OrderIntent? intent = await intents.GetForUpdate(claimed.Id, cancellationToken);

                if (intent is not { Status: OrderIntentStatus.Sending })
                {
                    return true;
                }

                intent.RecordFailure(now);
                await intents.Update(intent, cancellationToken);

                if (intent.Status == OrderIntentStatus.Failed && bot is not null && bot.Degrade(BotStatusReason.IntentFailed, now))
                {
                    await bots.Update(bot, cancellationToken);
                }

                return true;
            },
            cancellationToken);
    }

    private Task Degrade(BotId botId, BotStatusReason reason, CancellationToken cancellationToken)
    {
        return transactions.ExecuteAsync(
            async () =>
            {
                if (await bots.GetForUpdate(botId, cancellationToken) is { } bot && bot.Degrade(reason, clock.GetUtcNow()))
                {
                    await bots.Update(bot, cancellationToken);
                }

                return true;
            },
            cancellationToken);
    }

    private Task Release(OrderIntent claimed, CancellationToken cancellationToken)
    {
        return transactions.ExecuteAsync(
            async () =>
            {
                OrderIntent? intent = await intents.GetForUpdate(claimed.Id, cancellationToken);

                if (intent is not { Status: OrderIntentStatus.Sending })
                {
                    return true;
                }

                Bot? bot = await bots.Get(intent.BotId, cancellationToken);
                TradingHalt halt = await halts.Get(cancellationToken);
                bool killed = halt.IsHalted || (bot?.StatusReason is { } reason && BotStatusReasons.IsControl(reason));
                intent.Release(killed, clock.GetUtcNow());
                await intents.Update(intent, cancellationToken);

                return true;
            },
            cancellationToken);
    }

    private Task Change(OrderIntentId id, Action<OrderIntent> change, CancellationToken cancellationToken)
    {
        return transactions.ExecuteAsync(
            async () =>
            {
                OrderIntent? intent = await intents.GetForUpdate(id, cancellationToken);

                if (intent is { Status: OrderIntentStatus.Sending })
                {
                    change(intent);
                    await intents.Update(intent, cancellationToken);
                }

                return true;
            },
            cancellationToken);
    }

    /// <summary>
    ///     A submit leaves the process only for a bot that should run and is healthy, with no halt, under the claiming
    ///     holder's unexpired lease whose submit gate is open.
    /// </summary>
    private static bool CanPlace(Bot bot, BotLease? lease, LeaseHolderId holder, long epoch, TradingHalt halt, DateTimeOffset now)
    {
        return bot.CanSubmit(halt.IsHalted) && lease is not null && lease.IsHeldBy(holder, epoch, now) && lease.SubmissionsReady;
    }
}