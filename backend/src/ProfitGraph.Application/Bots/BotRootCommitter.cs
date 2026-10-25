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

                if (bot is { DesiredState: BotDesiredState.Stopped, StatusReason: { } reason } && BotStatusReasons.IsControl(reason))
                {
                    await killSwitch.Apply(bot.Id, commit.Checkpoint, cancellationToken);
                }

                await bots.Update(bot, cancellationToken);

                return true;
            },
            cancellationToken);
    }
}