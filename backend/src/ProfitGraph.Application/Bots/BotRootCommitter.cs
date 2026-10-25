using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     Writes a root in one fenced transaction (ADR 0016): it locks the lease and checks holder, epoch and expiry, then
///     saves the checkpoint, the new intents and the bot. A stale holder writes nothing. Domain events of everything
///     written reach the outbox in the same transaction.
/// </summary>
public sealed class BotRootCommitter(
    IBotLeaseRepository leases,
    IBotRepository bots,
    IBotCheckpointRepository checkpoints,
    IOrderIntentRepository intents,
    IManualOrderRequestRepository manualOrders,
    IBotFillRepository fills,
    IBotFeeCorrectionRepository feeCorrections,
    IBotPositionCycleRepository positions,
    BotKillSwitch killSwitch,
    ITransactionExecutor transactions,
    TimeProvider clock)
{
    /// <summary>
    ///     Commits <paramref name="commit" />; <paramref name="changeBot" /> applies the root's consequences to the locked
    ///     bot, such as a daily-loss check or a status report. A killed bot's new and waiting submits are rejected and its
    ///     open orders cancelled in the same transaction. Throws <see cref="BotLeaseLostException" /> when fenced.
    /// </summary>
    public Task Commit(BotRootCommit commit, Action<Bot>? changeBot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);

        return transactions.ExecuteAsync(
            async () =>
            {
                DateTimeOffset now = clock.GetUtcNow();
                BotLease? lease = await leases.GetForUpdate(commit.BotId, cancellationToken);

                if (lease is null || !lease.IsHeldBy(commit.HolderId, commit.Epoch, now))
                {
                    throw new BotLeaseLostException();
                }

                Bot bot = await bots.GetForUpdate(commit.BotId, cancellationToken) ?? throw new BotLeaseLostException();
                changeBot?.Invoke(bot);
                bot.RecordRun(now);
                await checkpoints.Save(new BotCheckpoint(commit.BotId, commit.Checkpoint, commit.AppliedParameters, now), cancellationToken);

                foreach (OrderIntent intent in commit.Intents)
                {
                    await intents.TryAdd(intent, cancellationToken);
                }

                if (commit.ManualOrder is { } manual)
                {
                    await Resolve(manual, now, cancellationToken);
                }

                foreach (BotFill fill in commit.Fills ?? [])
                {
                    await fills.TryAdd(fill, cancellationToken);
                }

                foreach (BotFeeCorrection correction in commit.FeeCorrections ?? [])
                {
                    await feeCorrections.TryAdd(correction, cancellationToken);
                }

                if (commit.Position is { } position)
                {
                    await Record(commit.BotId, position, cancellationToken);
                }

                if (bot is { DesiredState: BotDesiredState.Stopped, StatusReason: { } reason } && BotStatusReasons.IsControl(reason))
                {
                    await killSwitch.Apply(bot.Id, commit.Checkpoint, cancellationToken);
                }

                await bots.Update(bot, cancellationToken);

                return true;
            },
            cancellationToken);
    }

    /// <summary>
    ///     Resolves the manual order the root processed. A request that is no longer pending, because it expired while it
    ///     waited, fails the commit, so the engine restarts from its stored state without the order.
    /// </summary>
    private async Task Resolve(BotRootManualOrder manual, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ManualOrderRequest request = await manualOrders.GetForUpdate(manual.RequestId, cancellationToken) is { Status: ManualOrderStatus.Pending } pending
            ? pending
            : throw new BotRuleException(BotRuleException.ManualOrderResolved, "The manual order is no longer pending.");

        if (manual.EngineOrderId is { } engineOrderId)
        {
            request.MarkSubmitted(engineOrderId, now);
        }
        else
        {
            request.Reject(manual.RejectionReason ?? "rejected", now);
        }

        await manualOrders.Update(request, cancellationToken);
    }

    /// <summary>
    ///     Opens a cycle when the position opened, or closes the open one when it became flat.
    /// </summary>
    private async Task Record(BotId botId, BotRootPosition position, CancellationToken cancellationToken)
    {
        BotPositionCycle? open = await positions.GetOpenForUpdate(botId, cancellationToken);

        if (position.Change == BotPositionChange.Opened && open is null)
        {
            await positions.Add(BotPositionCycle.Open(BotPositionCycleId.New(), botId, position.At, position.RealizedPnl), cancellationToken);
        }
        else if (position.Change == BotPositionChange.Closed && open is not null && open.Close(position.At, position.RealizedPnl))
        {
            await positions.Update(open, cancellationToken);
        }
    }
}