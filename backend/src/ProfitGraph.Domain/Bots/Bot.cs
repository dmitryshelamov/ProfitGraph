using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     A live bot (ADR 0016): one strategy on one venue series, traded through the owner's credential. The owner sets the
///     desired state; the runtime that holds the bot's lease reports its runtime status. A degraded bot with a safety
///     reason waits for the owner, while a runtime failure restarts automatically with backoff.
/// </summary>
public sealed class Bot : AggregateRoot
{
    public BotId Id { get; }
    public UserId OwnerId { get; }
    public StrategyId StrategyId { get; }

    /// <summary>
    ///     The owner's name for the bot, or null while it has none; naming never touches trading or the desired state.
    /// </summary>
    public BotName? Name { get; private set; }

    /// <summary>
    ///     The strategy version the bot runs, or will run at its next start.
    /// </summary>
    public StrategyVersionId StrategyVersionId { get; private set; }

    public ExchangeCredentialId CredentialId { get; }
    public CandleSeries Series { get; }
    public BotParameters Parameters { get; private set; }
    public decimal AllocationCap { get; private set; }
    public BotRiskLimits RiskLimits { get; private set; }
    public BotFeeRates FeeRates { get; private set; }
    public bool AllowForeignOrders { get; private set; }

    /// <summary>
    ///     The venue module's settings, fixed when the bot is created.
    /// </summary>
    public BotVenueSettings VenueSettings { get; private init; } = BotVenueSettings.None;

    public BotDesiredState DesiredState { get; private set; }
    public BotRuntimeStatus RuntimeStatus { get; private set; }
    public BotStatusReason? StatusReason { get; private set; }
    public BotDiagnostics Diagnostics { get; private set; }
    public int RestartAttempts { get; private set; }
    public DateTimeOffset? NextRestartAt { get; private set; }

    /// <summary>
    ///     True once a run stored its first checkpoint; a later start resumes that run instead of starting it anew.
    /// </summary>
    public bool HasRun { get; private set; }

    public DateTimeOffset? FirstStartedAt { get; private set; }
    public DailyLossBaseline? DayBaseline { get; private set; }

    /// <summary>
    ///     The last saved strategy version the bot refused, so the same candidate is not tried and alerted again.
    /// </summary>
    public StrategyVersionId? RejectedVersionId { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsDegraded => RuntimeStatus == BotRuntimeStatus.Degraded;

    private Bot(
        BotId id,
        UserId ownerId,
        StrategyId strategyId,
        StrategyVersionId strategyVersionId,
        ExchangeCredentialId credentialId,
        CandleSeries series,
        BotParameters parameters,
        decimal allocationCap,
        BotRiskLimits riskLimits,
        BotFeeRates feeRates,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(riskLimits);
        ArgumentNullException.ThrowIfNull(feeRates);
        EnsurePositiveAllocation(allocationCap);
        Id = id;
        OwnerId = ownerId;
        StrategyId = strategyId;
        StrategyVersionId = strategyVersionId;
        CredentialId = credentialId;
        Series = series;
        Parameters = parameters;
        AllocationCap = allocationCap;
        RiskLimits = riskLimits;
        FeeRates = feeRates;
        DesiredState = BotDesiredState.Stopped;
        RuntimeStatus = BotRuntimeStatus.Created;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>
    ///     Creates a stopped bot that has never run.
    /// </summary>
    public static Bot Create(
        BotId id,
        UserId ownerId,
        StrategyId strategyId,
        StrategyVersionId strategyVersionId,
        ExchangeCredentialId credentialId,
        CandleSeries series,
        BotParameters parameters,
        decimal allocationCap,
        BotRiskLimits riskLimits,
        BotFeeRates feeRates,
        DateTimeOffset now,
        BotVenueSettings? venueSettings = null)
    {
        return new Bot(id, ownerId, strategyId, strategyVersionId, credentialId, series, parameters, allocationCap, riskLimits, feeRates, now)
        {
            VenueSettings = venueSettings ?? BotVenueSettings.None
        };
    }

    public static Bot Restore(BotSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!Enum.IsDefined(snapshot.DesiredState) || !Enum.IsDefined(snapshot.RuntimeStatus) ||
            snapshot.RestartAttempts is < 0 or > BotRestartSchedule.MaxAttempts || (snapshot.StatusReason is { } reason && !Enum.IsDefined(reason)))
        {
            throw new ArgumentException("Invalid stored bot.", nameof(snapshot));
        }

        return new Bot(
            snapshot.Id,
            snapshot.OwnerId,
            snapshot.StrategyId,
            snapshot.StrategyVersionId,
            snapshot.CredentialId,
            snapshot.Series,
            snapshot.Parameters,
            snapshot.AllocationCap,
            snapshot.RiskLimits,
            snapshot.FeeRates,
            snapshot.CreatedAt)
        {
            AllowForeignOrders = snapshot.AllowForeignOrders,
            VenueSettings = snapshot.VenueSettings ?? BotVenueSettings.None,
            Name = snapshot.Name,
            DesiredState = snapshot.DesiredState,
            RuntimeStatus = snapshot.RuntimeStatus,
            StatusReason = snapshot.StatusReason,
            Diagnostics = snapshot.Diagnostics,
            RestartAttempts = snapshot.RestartAttempts,
            NextRestartAt = snapshot.NextRestartAt,
            HasRun = snapshot.HasRun,
            FirstStartedAt = snapshot.FirstStartedAt,
            DayBaseline = snapshot.DayBaseline,
            RejectedVersionId = snapshot.RejectedVersionId,
            UpdatedAt = snapshot.UpdatedAt
        };
    }

    public bool IsOwnedBy(UserId userId)
    {
        return OwnerId == userId;
    }

    /// <summary>
    ///     Starts the bot. A bot without a checkpoint runs <paramref name="latestVersionId" />; a bot with one resumes its
    ///     stored version and changes graph at runtime. Starting an already running bot changes nothing, except that it
    ///     re-arms a bot degraded by a failed intent or an insufficient balance. Returns whether the bot changed.
    /// </summary>
    public bool Start(StrategyVersionId latestVersionId, bool hasCheckpoint, bool tradingHalted, DateTimeOffset now)
    {
        if (tradingHalted)
        {
            throw new BotRuleException(BotRuleException.TradingHalted, "Trading is halted; clear the global halt first.");
        }

        if (DesiredState == BotDesiredState.Running && !(IsDegraded && StatusReason is { } reason && BotStatusReasons.IsRestartableByStart(reason)))
        {
            return false;
        }

        if (!HasRun && FirstStartedAt is null)
        {
            FirstStartedAt = now;
        }

        if (!hasCheckpoint)
        {
            StrategyVersionId = latestVersionId;
        }

        DesiredState = BotDesiredState.Running;
        RuntimeStatus = BotRuntimeStatus.Created;
        StatusReason = null;
        Diagnostics = BotDiagnostics.None;
        RestartAttempts = 0;
        NextRestartAt = null;
        DayBaseline = null;
        UpdatedAt = now;
        AddDomainEvent(new BotStarted(Id, StrategyVersionId, now));

        return true;
    }

    /// <summary>
    ///     Stops the bot after its current root; its open orders stay as they are. Returns whether the bot changed.
    /// </summary>
    public bool Stop(DateTimeOffset now)
    {
        if (DesiredState == BotDesiredState.Stopped)
        {
            return false;
        }

        DesiredState = BotDesiredState.Stopped;
        UpdatedAt = now;
        AddDomainEvent(new BotStopped(Id, now));

        return true;
    }

    /// <summary>
    ///     A kill switch or the daily loss limit stops the bot at once. The caller cancels its open orders and rejects its
    ///     unplaced submits; the position is never sold. Only an explicit start clears the reason.
    /// </summary>
    public void Kill(BotStatusReason reason, DateTimeOffset now)
    {
        if (!BotStatusReasons.IsControl(reason))
        {
            throw new ArgumentException("A kill needs a control reason.", nameof(reason));
        }

        DesiredState = BotDesiredState.Stopped;
        RuntimeStatus = BotRuntimeStatus.Stopped;
        StatusReason = reason;
        Diagnostics = BotDiagnostics.None;
        NextRestartAt = null;
        UpdatedAt = now;
        AddDomainEvent(new BotKilled(Id, reason, now));
    }

    /// <summary>
    ///     Whether a runtime may take the bot's lease: it should run, and it is not waiting for the owner or for its next
    ///     restart time.
    /// </summary>
    public bool CanAcquireLease(DateTimeOffset now)
    {
        if (DesiredState != BotDesiredState.Running)
        {
            return false;
        }

        return !IsDegraded || (StatusReason is { } reason && !BotStatusReasons.IsSafety(reason) &&
            RestartAttempts < BotRestartSchedule.MaxAttempts && NextRestartAt is { } next && next <= now);
    }

    /// <summary>
    ///     Counts a restart when a runtime takes the lease of a degraded bot.
    /// </summary>
    public void RecordLeaseAcquired(DateTimeOffset now)
    {
        if (!CanAcquireLease(now))
        {
            throw new BotRuleException(BotRuleException.NotLeasable, "The bot cannot be leased now.");
        }

        if (IsDegraded)
        {
            RestartAttempts++;
        }

        UpdatedAt = now;
    }

    public bool MarkStarting(DateTimeOffset now)
    {
        if (DesiredState != BotDesiredState.Running)
        {
            return false;
        }

        RuntimeStatus = BotRuntimeStatus.Starting;
        StatusReason = null;
        UpdatedAt = now;
        AddDomainEvent(new BotRuntimeStarting(Id, now));

        return true;
    }

    /// <summary>
    ///     The runtime opened the engine. A degraded bot cannot report running directly; it restarts through a new lease.
    /// </summary>
    public bool MarkRunning(DateTimeOffset now)
    {
        if (DesiredState != BotDesiredState.Running || IsDegraded)
        {
            return false;
        }

        bool recovered = RuntimeStatus != BotRuntimeStatus.Running && RestartAttempts > 0;
        bool changed = RuntimeStatus != BotRuntimeStatus.Running;
        RuntimeStatus = BotRuntimeStatus.Running;
        StatusReason = null;
        UpdatedAt = now;

        if (recovered)
        {
            AddDomainEvent(new BotRecovered(Id, now));
        }

        return changed;
    }

    /// <summary>
    ///     The runtime stopped processing the bot. A kill reason stays visible until the next start.
    /// </summary>
    public bool MarkRuntimeStopped(DateTimeOffset now)
    {
        if (RuntimeStatus == BotRuntimeStatus.Stopped)
        {
            return false;
        }

        RuntimeStatus = BotRuntimeStatus.Stopped;

        if (StatusReason is { } reason && !BotStatusReasons.IsControl(reason))
        {
            StatusReason = null;
        }

        UpdatedAt = now;
        AddDomainEvent(new BotRuntimeStopped(Id, now));

        return true;
    }

    /// <summary>
    ///     A runtime failure degrades the bot and schedules an automatic restart, unless a safety reason already holds it.
    ///     <paramref name="detail" /> is a short text that is safe to show to the owner (ADR 0029).
    /// </summary>
    public bool RecordRuntimeFailure(DateTimeOffset now, string? detail = null)
    {
        if (IsDegraded && StatusReason is { } held && BotStatusReasons.IsSafety(held))
        {
            return false;
        }

        bool wasDegraded = IsDegraded;
        RuntimeStatus = BotRuntimeStatus.Degraded;
        StatusReason = BotStatusReason.RuntimeFailure;
        NextRestartAt = BotRestartSchedule.NextRestartAt(RestartAttempts, now);
        UpdatedAt = now;

        if (!wasDegraded)
        {
            AddDomainEvent(new BotDegraded(Id, BotStatusReason.RuntimeFailure, now, FailureDetail.Limit(detail)));
        }

        return true;
    }

    /// <summary>
    ///     A confirmed unsafe condition degrades the bot until the owner starts it again.
    /// </summary>
    public bool Degrade(BotStatusReason reason, DateTimeOffset now, string? detail = null)
    {
        if (!BotStatusReasons.IsSafety(reason))
        {
            throw new ArgumentException("Degrade takes a safety reason.", nameof(reason));
        }

        if (DesiredState != BotDesiredState.Running || (IsDegraded && StatusReason == reason))
        {
            return false;
        }

        RuntimeStatus = BotRuntimeStatus.Degraded;
        StatusReason = reason;
        NextRestartAt = null;
        UpdatedAt = now;
        AddDomainEvent(new BotDegraded(Id, reason, now, FailureDetail.Limit(detail)));

        return true;
    }

    /// <summary>
    ///     Returns a bot that the venue account's protection stopped to Created once the protection is resumed, so the runtime
    ///     takes it again and reconciles before sending. Any other state is left as it is. Returns whether the bot changed.
    /// </summary>
    public bool ClearVenueAccountProtection(DateTimeOffset now)
    {
        if (DesiredState != BotDesiredState.Running || !IsDegraded || StatusReason != BotStatusReason.VenueAccountProtection)
        {
            return false;
        }

        RuntimeStatus = BotRuntimeStatus.Created;
        StatusReason = null;
        NextRestartAt = null;
        UpdatedAt = now;
        AddDomainEvent(new BotRecovered(Id, now));

        return true;
    }

    /// <summary>
    ///     Records that the bot continued on a restated last closed candle (ADR 0029).
    /// </summary>
    public void RecordHistoryRestated(DateTimeOffset openTime, decimal previousClose, decimal restatedClose, DateTimeOffset now)
    {
        UpdatedAt = now;
        AddDomainEvent(new BotHistoryRestated(Id, openTime, previousClose, restatedClose, now));
    }

    /// <summary>
    ///     A root saved after a live closed candle proves the bot works again, so the restart streak starts over.
    /// </summary>
    public void ResetRestartStreak(DateTimeOffset now)
    {
        RestartAttempts = 0;
        NextRestartAt = null;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Records the equity at a live candle root. The first root of a UTC day sets the baseline; a later root whose
    ///     loss from it reaches the daily limit kills the bot. Returns whether the bot was killed.
    /// </summary>
    public bool RecordDayEquity(DateOnly day, decimal equity, DateTimeOffset now)
    {
        if (DesiredState != BotDesiredState.Running)
        {
            return false;
        }

        if (DayBaseline is null || DayBaseline.Day != day)
        {
            DayBaseline = new DailyLossBaseline(day, equity);
            UpdatedAt = now;

            return false;
        }

        if (DayBaseline.Equity - equity < RiskLimits.DailyLossLimitQuote)
        {
            return false;
        }

        Kill(BotStatusReason.DailyLossLimit, now);

        return true;
    }

    /// <summary>
    ///     Marks that a run stored its first checkpoint.
    /// </summary>
    public void RecordRun(DateTimeOffset now)
    {
        if (HasRun)
        {
            return;
        }

        HasRun = true;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Forgets a stopped run that holds and spent nothing (Profit Fox Designer ADR 0068, widened), so the next start runs
    ///     <c>OnStart</c> with the current parameters. The caller proves the run holds nothing, left no fill, has only final
    ///     intents and holds no lease.
    /// </summary>
    public void ForgetUntradedRun(DateTimeOffset now)
    {
        EnsureStopped();
        HasRun = false;
        FirstStartedAt = null;
        UpdatedAt = now;
        AddDomainEvent(new BotRunRestarted(Id, now));
    }

    public void ChangeParameters(BotParameters parameters, decimal allocationCap, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        EnsureStopped();
        EnsurePositiveAllocation(allocationCap);
        Parameters = parameters;
        AllocationCap = allocationCap;
        UpdatedAt = now;
        AddDomainEvent(new BotParametersChanged(Id, now));
    }

    /// <summary>
    ///     Names the bot, or clears the name with null. The name may change in any state: it is not part of trading, the desired
    ///     state or the run. Returns whether the name changed.
    /// </summary>
    public bool Rename(BotName? name, DateTimeOffset now)
    {
        if (name == Name)
        {
            return false;
        }

        Name = name;
        UpdatedAt = now;

        return true;
    }

    /// <summary>
    ///     Risk limits may change while the bot runs; the next submit checks the new ones.
    /// </summary>
    public void ChangeRiskLimits(BotRiskLimits riskLimits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(riskLimits);

        if (riskLimits == RiskLimits)
        {
            return;
        }

        RiskLimits = riskLimits;
        UpdatedAt = now;
        AddDomainEvent(new BotRiskLimitsChanged(Id, now));
    }

    /// <summary>
    ///     Allows or forbids foreign orders on the bot's symbol (stage 5 plan S5-16); only a stopped bot changes it.
    /// </summary>
    public void ChangeForeignOrdersPolicy(bool allowForeignOrders, DateTimeOffset now)
    {
        if (DesiredState != BotDesiredState.Stopped)
        {
            throw new BotRuleException(BotRuleException.MustBeStopped, "Stop the bot before changing symbol sharing.");
        }

        if (AllowForeignOrders == allowForeignOrders)
        {
            return;
        }

        AllowForeignOrders = allowForeignOrders;
        UpdatedAt = now;
        AddDomainEvent(new BotForeignOrdersPolicyChanged(Id, allowForeignOrders, now));
    }

    /// <summary>
    ///     Stores rates read from the venue; an event is raised only when the rates differ.
    /// </summary>
    public void ChangeFeeRates(BotFeeRates feeRates, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(feeRates);
        bool changed = !feeRates.HasSameRates(FeeRates);
        FeeRates = feeRates;
        UpdatedAt = now;

        if (changed)
        {
            AddDomainEvent(new BotFeeRatesChanged(Id, now));
        }
    }

    /// <summary>
    ///     The engine now runs <paramref name="versionId" />, after a start or a graph change. Every root reports the version
    ///     it ran, so only a switch clears an earlier rejection.
    /// </summary>
    public void ApplyStrategyVersion(StrategyVersionId versionId, DateTimeOffset now)
    {
        if (versionId == StrategyVersionId)
        {
            return;
        }

        Diagnostics &= ~BotDiagnostics.GraphChangeRejected;
        StrategyVersionId = versionId;
        UpdatedAt = now;
        AddDomainEvent(new BotGraphVersionApplied(Id, versionId, now));
    }

    /// <summary>
    ///     A saved version could not be applied; the bot keeps its version and the same candidate is refused only once.
    ///     Returns whether this candidate was new.
    /// </summary>
    public bool RejectStrategyVersion(StrategyVersionId versionId, DateTimeOffset now)
    {
        if (RejectedVersionId == versionId)
        {
            return false;
        }

        RejectedVersionId = versionId;
        Diagnostics |= BotDiagnostics.GraphChangeRejected;
        UpdatedAt = now;
        AddDomainEvent(new BotGraphChangeRejected(Id, versionId, now));

        return true;
    }

    public bool MarkFeedStale(bool stale, DateTimeOffset now)
    {
        if (DesiredState != BotDesiredState.Running || Diagnostics.HasFlag(BotDiagnostics.StaleFeed) == stale)
        {
            return false;
        }

        Diagnostics = stale ? Diagnostics | BotDiagnostics.StaleFeed : Diagnostics & ~BotDiagnostics.StaleFeed;
        UpdatedAt = now;
        AddDomainEvent(stale ? new BotFeedStalled(Id, now) : new BotFeedRecovered(Id, now));

        return true;
    }

    /// <summary>
    ///     Marks the venue temporarily unavailable or available again; only the first report of a streak raises an event.
    /// </summary>
    public bool MarkExchangeUnavailable(bool unavailable, DateTimeOffset now)
    {
        if (Diagnostics.HasFlag(BotDiagnostics.ExchangeUnavailable) == unavailable)
        {
            return false;
        }

        Diagnostics = unavailable ? Diagnostics | BotDiagnostics.ExchangeUnavailable : Diagnostics & ~BotDiagnostics.ExchangeUnavailable;
        UpdatedAt = now;

        if (unavailable)
        {
            AddDomainEvent(new BotExchangeUnavailable(Id, now));
        }

        return true;
    }

    /// <summary>
    ///     A stopped bot keeps its waiting submits instead of sending them (cancels still go out).
    /// </summary>
    public bool HoldSubmits(DateTimeOffset now)
    {
        if (DesiredState != BotDesiredState.Stopped || Diagnostics.HasFlag(BotDiagnostics.HeldSubmit))
        {
            return false;
        }

        Diagnostics |= BotDiagnostics.HeldSubmit;
        UpdatedAt = now;
        AddDomainEvent(new BotSubmitHeld(Id, now));

        return true;
    }

    /// <summary>
    ///     Whether a submit may be claimed or placed for this bot now.
    /// </summary>
    public bool CanSubmit(bool tradingHalted)
    {
        return !tradingHalted && DesiredState == BotDesiredState.Running && !IsDegraded;
    }

    /// <summary>
    ///     A manual order (Profit Fox Designer ADR 0091) is accepted only while the bot should run and runs.
    /// </summary>
    public void EnsureAcceptsManualOrders()
    {
        if (DesiredState != BotDesiredState.Running || RuntimeStatus != BotRuntimeStatus.Running)
        {
            throw new BotRuleException(BotRuleException.NotRunning, "A manual order needs a running bot.");
        }
    }

    public BotSnapshot ToSnapshot()
    {
        return new BotSnapshot(
            Id,
            OwnerId,
            StrategyId,
            StrategyVersionId,
            CredentialId,
            Series,
            Parameters,
            AllocationCap,
            RiskLimits,
            FeeRates,
            AllowForeignOrders,
            DesiredState,
            RuntimeStatus,
            StatusReason,
            Diagnostics,
            RestartAttempts,
            NextRestartAt,
            HasRun,
            FirstStartedAt,
            DayBaseline,
            RejectedVersionId,
            CreatedAt,
            UpdatedAt,
            VenueSettings,
            Name);
    }

    /// <summary>
    ///     Parameters, allocation and the run itself change only while the bot should stop and its runtime stopped.
    /// </summary>
    public void EnsureStopped()
    {
        if (DesiredState != BotDesiredState.Stopped || RuntimeStatus is BotRuntimeStatus.Starting or BotRuntimeStatus.Running)
        {
            throw new BotRuleException(BotRuleException.MustBeStopped, "Stop the bot and wait for its runtime to stop first.");
        }
    }

    private static void EnsurePositiveAllocation(decimal allocationCap)
    {
        if (allocationCap <= 0m)
        {
            throw new BotRuleException(BotRuleException.InvalidSettings, "The allocation cap must be positive.");
        }
    }
}