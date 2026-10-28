using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.Leases;

/// <summary>
///     The right of one runtime process to execute a bot (ADR 0016). Taking over an expired lease increases the epoch,
///     and every checkpoint write checks holder, epoch and expiry, so a stale holder cannot commit. The submit gate opens
///     only after the holder reconciled the bot's venue state.
/// </summary>
public sealed class BotLease : AggregateRoot
{
    public BotId BotId { get; }
    public LeaseHolderId HolderId { get; private set; }
    public long Epoch { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool SubmissionsReady { get; private set; }

    private BotLease(BotId botId, LeaseHolderId holderId, long epoch, DateTimeOffset expiresAt, bool submissionsReady)
    {
        if (epoch < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch), "A lease epoch starts at 1.");
        }

        BotId = botId;
        HolderId = holderId;
        Epoch = epoch;
        ExpiresAt = expiresAt;
        SubmissionsReady = submissionsReady;
    }

    /// <summary>
    ///     The first lease of a bot.
    /// </summary>
    public static BotLease Acquire(BotId botId, LeaseHolderId holderId, DateTimeOffset now, TimeSpan duration)
    {
        EnsurePositive(duration);

        return new BotLease(botId, holderId, epoch: 1, now + duration, submissionsReady: false);
    }

    public static BotLease Restore(BotId botId, LeaseHolderId holderId, long epoch, DateTimeOffset expiresAt, bool submissionsReady)
    {
        return new BotLease(botId, holderId, epoch, expiresAt, submissionsReady);
    }

    public bool IsExpired(DateTimeOffset now)
    {
        return ExpiresAt <= now;
    }

    public bool IsHeldBy(LeaseHolderId holderId, long epoch, DateTimeOffset now)
    {
        return HolderId == holderId && Epoch == epoch && !IsExpired(now);
    }

    /// <summary>
    ///     Takes over an expired lease under the next epoch with a closed submit gate.
    /// </summary>
    public void TakeOver(LeaseHolderId holderId, DateTimeOffset now, TimeSpan duration)
    {
        EnsurePositive(duration);

        if (!IsExpired(now))
        {
            throw new BotRuleException(BotRuleException.LeaseHeld, "Another runtime holds the bot's lease.");
        }

        HolderId = holderId;
        Epoch++;
        ExpiresAt = now + duration;
        SubmissionsReady = false;
    }

    /// <summary>
    ///     Extends the lease for its current holder and epoch while it has not expired. Returns false otherwise; the
    ///     holder must then stop executing the bot.
    /// </summary>
    public bool Renew(LeaseHolderId holderId, long epoch, DateTimeOffset now, TimeSpan duration)
    {
        EnsurePositive(duration);

        if (!IsHeldBy(holderId, epoch, now))
        {
            return false;
        }

        ExpiresAt = now + duration;

        return true;
    }

    /// <summary>
    ///     Gives the lease up so another runtime may take it at once: it expires now. Only its holder can release it.
    /// </summary>
    public bool Release(LeaseHolderId holderId, long epoch, DateTimeOffset now)
    {
        if (HolderId != holderId || Epoch != epoch)
        {
            return false;
        }

        ExpiresAt = ExpiresAt < now ? ExpiresAt : now;
        SubmissionsReady = false;

        return true;
    }

    /// <summary>
    ///     Opens the submit gate after reconciliation, for the current holder only.
    /// </summary>
    public bool OpenSubmitGate(LeaseHolderId holderId, long epoch, DateTimeOffset now)
    {
        if (!IsHeldBy(holderId, epoch, now))
        {
            return false;
        }

        SubmissionsReady = true;

        return true;
    }

    /// <summary>
    ///     Closes the submit gate, for example when the venue stream disconnects or a kill switch fires.
    /// </summary>
    public void CloseSubmitGate()
    {
        SubmissionsReady = false;
    }

    private static void EnsurePositive(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "A lease lasts a positive time.");
        }
    }
}