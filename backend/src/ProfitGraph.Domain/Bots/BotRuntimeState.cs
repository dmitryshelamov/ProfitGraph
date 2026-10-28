namespace ProfitGraph.Domain.Bots;

/// <summary>
///     What the runtime reports about a bot (ADR 0016): its status, the reason that holds or stopped it, and the restart
///     streak after runtime failures (ADR 0050). Each transition returns the next state, or null when the current state
///     refuses it; the bot adds its desired state, its events and its timestamps. A safety reason waits for the owner, and a
///     control reason stays until the next start.
/// </summary>
public sealed record BotRuntimeState(BotRuntimeStatus Status, BotStatusReason? Reason, int RestartAttempts, DateTimeOffset? NextRestartAt)
{
    public static BotRuntimeState Created { get; } = new BotRuntimeState(BotRuntimeStatus.Created, Reason: null, RestartAttempts: 0, NextRestartAt: null);

    public bool IsDegraded => Status == BotRuntimeStatus.Degraded;

    public bool HoldsSafetyReason => IsDegraded && Reason is { } reason && BotStatusReasons.IsSafety(reason);

    /// <summary>
    ///     Whether a start re-arms the bot although it should already run: a failed intent or an insufficient balance.
    /// </summary>
    public bool IsRestartableByStart => IsDegraded && Reason is { } reason && BotStatusReasons.IsRestartableByStart(reason);

    /// <summary>
    ///     Whether a runtime may take the lease: the bot is not degraded, or a runtime failure's restart time came. A runtime
    ///     failure without a restart time, left by the former five-restart limit, is due at once (ADR 0050).
    /// </summary>
    public bool IsLeasable(DateTimeOffset now)
    {
        return !IsDegraded || (Reason == BotStatusReason.RuntimeFailure && (NextRestartAt is not { } next || next <= now));
    }

    /// <summary>
    ///     A start clears the reason and the restart streak. A runtime that is still starting or running, because the bot was
    ///     started again before its runner stopped, keeps its status.
    /// </summary>
    public BotRuntimeState Start()
    {
        BotRuntimeStatus status = Status is BotRuntimeStatus.Starting or BotRuntimeStatus.Running ? Status : BotRuntimeStatus.Created;

        return new BotRuntimeState(status, Reason: null, RestartAttempts: 0, NextRestartAt: null);
    }

    public BotRuntimeState Kill(BotStatusReason reason)
    {
        if (!BotStatusReasons.IsControl(reason))
        {
            throw new ArgumentException("A kill needs a control reason.", nameof(reason));
        }

        return this with { Status = BotRuntimeStatus.Stopped, Reason = reason, NextRestartAt = null };
    }

    /// <summary>
    ///     Counts a restart when a runtime takes the lease of a degraded bot.
    /// </summary>
    public BotRuntimeState LeaseAcquired()
    {
        return IsDegraded && RestartAttempts < BotRestartSchedule.MaxCountedAttempts ? this with { RestartAttempts = RestartAttempts + 1 } : this;
    }

    /// <summary>
    ///     The runtime opens the bot; a safety reason that degraded it meanwhile refuses.
    /// </summary>
    public BotRuntimeState? MarkStarting()
    {
        return HoldsSafetyReason ? null : this with { Status = BotRuntimeStatus.Starting, Reason = null };
    }

    /// <summary>
    ///     The runtime opened the engine. A degraded bot cannot report running directly; it restarts through a new lease.
    /// </summary>
    public BotRuntimeState? MarkRunning()
    {
        return IsDegraded ? null : this with { Status = BotRuntimeStatus.Running, Reason = null };
    }

    /// <summary>
    ///     The runtime stopped. A control reason stays; a bot degraded by a safety reason stays degraded.
    /// </summary>
    public BotRuntimeState? MarkStopped()
    {
        if (Status == BotRuntimeStatus.Stopped || HoldsSafetyReason)
        {
            return null;
        }

        return this with { Status = BotRuntimeStatus.Stopped, Reason = Reason is { } reason && BotStatusReasons.IsControl(reason) ? reason : null };
    }

    /// <summary>
    ///     A runtime failure degrades the bot until its next restart time, unless a safety reason holds it or a kill stopped it.
    /// </summary>
    public BotRuntimeState? RecordFailure(DateTimeOffset now)
    {
        if (HoldsSafetyReason || (Reason is { } held && BotStatusReasons.IsControl(held)))
        {
            return null;
        }

        return this with
        {
            Status = BotRuntimeStatus.Degraded,
            Reason = BotStatusReason.RuntimeFailure,
            NextRestartAt = BotRestartSchedule.NextRestartAt(RestartAttempts, now)
        };
    }

    /// <summary>
    ///     A confirmed unsafe condition degrades the bot until the owner starts it again; the same reason is recorded once.
    /// </summary>
    public BotRuntimeState? Degrade(BotStatusReason reason)
    {
        if (!BotStatusReasons.IsSafety(reason))
        {
            throw new ArgumentException("Degrade takes a safety reason.", nameof(reason));
        }

        return IsDegraded && Reason == reason ? null : this with { Status = BotRuntimeStatus.Degraded, Reason = reason, NextRestartAt = null };
    }

    /// <summary>
    ///     Returns a bot degraded by <paramref name="reason" /> to Created on the venue's evidence, so a runtime takes it
    ///     again; <paramref name="resetRestarts" /> also starts the restart streak over. Any other state refuses.
    /// </summary>
    public BotRuntimeState? Clear(BotStatusReason reason, bool resetRestarts)
    {
        if (!IsDegraded || Reason != reason)
        {
            return null;
        }

        return new BotRuntimeState(BotRuntimeStatus.Created, Reason: null, resetRestarts ? 0 : RestartAttempts, NextRestartAt: null);
    }

    /// <summary>
    ///     A root saved after a live closed candle proves the bot works again, so the restart streak starts over.
    /// </summary>
    public BotRuntimeState ResetRestartStreak()
    {
        return this with { RestartAttempts = 0, NextRestartAt = null };
    }
}