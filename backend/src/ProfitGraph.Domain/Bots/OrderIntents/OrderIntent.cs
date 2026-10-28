using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Common;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Domain.Bots.OrderIntents;

/// <summary>
///     One command to a venue, stored in the same transaction as the engine checkpoint that produced it (ADR 0016): the
///     order outbox. A dispatcher claims it, delivers it and records the outcome. A submit whose place request may have
///     left the process is never placed again without looking it up first. The owner may confirm such a submit absent at the
///     venue; reconciliation still looks it up first and rejects it only when the venue shows nothing (Profit Fox Designer
///     ADR 0055).
/// </summary>
public sealed class OrderIntent : AggregateRoot
{
    public OrderIntentId Id { get; }
    public BotId BotId { get; }
    public long EngineOrderId { get; }
    public OrderIntentKind Kind { get; }
    public ClientOrderId ClientOrderId { get; }

    /// <summary>
    ///     The engine's order for a submit; a cancel carries none.
    /// </summary>
    public OrderSnapshot? Order { get; }

    /// <summary>
    ///     True for a cancel written by a kill switch.
    /// </summary>
    public bool FromKill { get; }

    public OrderIntentStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }

    /// <summary>
    ///     When a place request first may have left the process; from then on every retry looks the order up first.
    /// </summary>
    public DateTimeOffset? PlaceStartedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }
    public OrderIntentRejection? Rejection { get; private set; }
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    ///     When the owner confirmed the submit absent at the venue, and who.
    /// </summary>
    public DateTimeOffset? ConfirmedAbsentAt { get; private set; }

    public UserId? ConfirmedAbsentBy { get; private set; }

    /// <summary>
    ///     True when the gateway closed the order on its own evidence instead of the owner's confirmation (ADR 0030): then
    ///     <see cref="ConfirmedAbsentAt" /> is set and <see cref="ConfirmedAbsentBy" /> is not.
    /// </summary>
    public bool ConfirmedAbsentAutomatically { get; private set; }

    /// <summary>
    ///     Why a deferred submit waits; its next check is <see cref="NextAttemptAt" />.
    /// </summary>
    public OrderDeferral? Deferral { get; private set; }

    public bool MustLookUpBeforeSubmit => PlaceStartedAt is not null;

    private OrderIntent(
        OrderIntentId id,
        BotId botId,
        long engineOrderId,
        OrderIntentKind kind,
        ClientOrderId clientOrderId,
        OrderSnapshot? order,
        bool fromKill,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(clientOrderId);

        if (engineOrderId <= 0 || !Enum.IsDefined(kind) || kind == OrderIntentKind.Submit != order is not null ||
            (order is not null && order.Id != engineOrderId) || (fromKill && kind != OrderIntentKind.Cancel))
        {
            throw new BotRuleException(BotRuleException.InvalidIntentTransition, "Invalid order intent.");
        }

        Id = id;
        BotId = botId;
        EngineOrderId = engineOrderId;
        Kind = kind;
        ClientOrderId = clientOrderId;
        Order = order;
        FromKill = fromKill;
        Status = OrderIntentStatus.Pending;
        CreatedAt = createdAt;
    }

    public static OrderIntent Submit(OrderIntentId id, BotId botId, OrderSnapshot order, ClientOrderId clientOrderId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderIntent(id, botId, order.Id, OrderIntentKind.Submit, clientOrderId, order, fromKill: false, now);
    }

    public static OrderIntent Cancel(OrderIntentId id, BotId botId, long engineOrderId, ClientOrderId clientOrderId, bool fromKill, DateTimeOffset now)
    {
        return new OrderIntent(id, botId, engineOrderId, OrderIntentKind.Cancel, clientOrderId, order: null, fromKill, now);
    }

    public static OrderIntent Restore(OrderIntentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!Enum.IsDefined(snapshot.Status) || snapshot.Attempts is < 0 or > OrderIntentRetrySchedule.MaxAttempts ||
            (snapshot.Status == OrderIntentStatus.Deferred &&
                !(snapshot.Deferral is { IsValid: true } && snapshot.Kind == OrderIntentKind.Submit && snapshot.PlaceStartedAt is null)))
        {
            throw new ArgumentException("Invalid stored order intent.", nameof(snapshot));
        }

        return new OrderIntent(
            snapshot.Id,
            snapshot.BotId,
            snapshot.EngineOrderId,
            snapshot.Kind,
            snapshot.ClientOrderId,
            snapshot.Order,
            snapshot.FromKill,
            snapshot.CreatedAt)
        {
            Status = snapshot.Status,
            Attempts = snapshot.Attempts,
            NextAttemptAt = snapshot.NextAttemptAt,
            ClaimedAt = snapshot.ClaimedAt,
            PlaceStartedAt = snapshot.PlaceStartedAt,
            SentAt = snapshot.SentAt,
            Rejection = snapshot.Rejection,
            ConfirmedAbsentAt = snapshot.ConfirmedAbsentAt,
            ConfirmedAbsentBy = snapshot.ConfirmedAbsentBy,
            ConfirmedAbsentAutomatically = snapshot.ConfirmedAbsentAutomatically,
            Deferral = snapshot.Deferral
        };
    }

    /// <summary>
    ///     A due pending intent, or one whose claim went stale because its dispatcher stopped, can be claimed.
    /// </summary>
    public bool CanClaim(DateTimeOffset now)
    {
        return Status switch
        {
            OrderIntentStatus.Pending => NextAttemptAt is null || NextAttemptAt <= now,
            OrderIntentStatus.Deferred => NextAttemptAt <= now,
            OrderIntentStatus.Sending => ClaimedAt <= now - OrderIntentRetrySchedule.StaleClaimAfter,
            _ => false
        };
    }

    public void Claim(DateTimeOffset now)
    {
        if (!CanClaim(now))
        {
            throw Invalid("The intent cannot be claimed now.");
        }

        Status = OrderIntentStatus.Sending;
        ClaimedAt = now;
    }

    /// <summary>
    ///     A claimed submit that has not started placing cannot go yet (ADR 0024): it waits for <paramref name="deferral" />
    ///     and is claimed again at <paramref name="nextCheckAt" />. A new reason starts a new wait; the same reason keeps its
    ///     start. Returns whether the wait is new or its reason changed, which the core reports.
    /// </summary>
    public bool Defer(OrderDeferral deferral, DateTimeOffset nextCheckAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deferral);
        EnsureClaimedSubmit();

        if (PlaceStartedAt is not null || !deferral.IsValid || nextCheckAt < now)
        {
            throw Invalid("A submit that may have left the process, or an invalid wait, cannot be deferred.");
        }

        bool started = Deferral?.Code != deferral.Code;
        Deferral = started ? deferral : deferral with { Since = Deferral!.Since };
        Status = OrderIntentStatus.Deferred;
        ClaimedAt = null;
        NextAttemptAt = nextCheckAt;

        if (started)
        {
            AddDomainEvent(new OrderIntentDeferred(BotId, Id, EngineOrderId, deferral.Code, deferral.Until, now));
        }

        return started;
    }

    /// <summary>
    ///     The strategy cancelled a submit that is still deferred: it is rejected locally, with nothing at the venue to cancel.
    /// </summary>
    public void CancelDeferred(DateTimeOffset now)
    {
        if (Status != OrderIntentStatus.Deferred)
        {
            throw Invalid("Only a deferred submit is cancelled locally.");
        }

        RejectNow(new OrderIntentRejection(RejectionReason.CancelledBeforeSend, "The order was cancelled before it was sent."), now);
    }

    /// <summary>
    ///     Records, before the place request leaves the process, that it may reach the venue. Only the first time raises
    ///     an event.
    /// </summary>
    public void MarkPlaceStarted(DateTimeOffset now)
    {
        EnsureClaimedSubmit();

        if (PlaceStartedAt is not null)
        {
            return;
        }

        PlaceStartedAt = now;
        Deferral = null;
        AddDomainEvent(new OrderIntentPlaceStarted(BotId, Id, EngineOrderId, now));
    }

    public void MarkSent(DateTimeOffset now)
    {
        if (Status != OrderIntentStatus.Sending)
        {
            throw Invalid("Only a claimed intent is sent.");
        }

        Status = OrderIntentStatus.Sent;
        Deferral = null;
        SentAt = now;
        ClaimedAt = null;
        NextAttemptAt = null;
    }

    /// <summary>
    ///     A definitive rejection, by a risk check or by the venue; the engine receives it as an order rejection.
    /// </summary>
    public void Reject(OrderIntentRejection rejection, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rejection);
        EnsureClaimedSubmit();
        RejectNow(rejection, now);
    }

    /// <summary>
    ///     A delivery failed. The intent waits for its next attempt, or fails after the last one.
    /// </summary>
    public void RecordFailure(DateTimeOffset now)
    {
        if (Status != OrderIntentStatus.Sending)
        {
            throw Invalid("Only a claimed intent fails.");
        }

        TimeSpan delay = OrderIntentRetrySchedule.DelayAfter(Attempts);
        Attempts++;
        ClaimedAt = null;

        if (Attempts >= OrderIntentRetrySchedule.MaxAttempts)
        {
            Status = OrderIntentStatus.Failed;
            NextAttemptAt = null;
            AddDomainEvent(new OrderIntentFailed(BotId, Id, EngineOrderId, now));

            return;
        }

        Status = OrderIntentStatus.Pending;
        NextAttemptAt = now + delay;
    }

    /// <summary>
    ///     The submit could not be checked now (for example, the venue balance is unknown); it waits without counting a
    ///     failure.
    /// </summary>
    public void RetryLater(DateTimeOffset nextAttemptAt)
    {
        EnsureClaimedSubmit();
        Status = OrderIntentStatus.Pending;
        ClaimedAt = null;
        NextAttemptAt = nextAttemptAt;
    }

    /// <summary>
    ///     Gives a claimed intent back, for example on shutdown or when the submit gate is closed. A submit that never left
    ///     the process is rejected instead when a kill or the global halt blocks submits.
    /// </summary>
    public void Release(bool submitsKilled, DateTimeOffset now)
    {
        if (Status != OrderIntentStatus.Sending)
        {
            throw Invalid("Only a claimed intent is released.");
        }

        if (submitsKilled && Kind == OrderIntentKind.Submit && PlaceStartedAt is null)
        {
            RejectNow(KillRejection(), now);

            return;
        }

        Status = OrderIntentStatus.Pending;
        ClaimedAt = null;
    }

    /// <summary>
    ///     A kill rejects a waiting or failed submit that never left the process. Returns whether it did.
    /// </summary>
    public bool RejectForKill(DateTimeOffset now)
    {
        if (Kind != OrderIntentKind.Submit || PlaceStartedAt is not null ||
            Status is not (OrderIntentStatus.Pending or OrderIntentStatus.Deferred or OrderIntentStatus.Failed))
        {
            return false;
        }

        RejectNow(KillRejection(), now);

        return true;
    }

    /// <summary>
    ///     Reconciliation established the order's state at the venue, so the intent needs no further delivery.
    /// </summary>
    public void Resolve(DateTimeOffset now)
    {
        if (Status is not (OrderIntentStatus.Pending or OrderIntentStatus.Sending or OrderIntentStatus.Failed))
        {
            throw Invalid("Only an undelivered intent is resolved.");
        }

        Status = OrderIntentStatus.Sent;
        SentAt = now;
        ClaimedAt = null;
        NextAttemptAt = null;
    }

    /// <summary>
    ///     The owner confirms that a submit which may have left the process never reached the venue: a failed one, or one
    ///     waiting to be looked up. It is not delivered again; the next reconciliation decides.
    /// </summary>
    public void ConfirmAbsent(UserId owner, DateTimeOffset now)
    {
        if (Kind != OrderIntentKind.Submit || PlaceStartedAt is null || ConfirmedAbsentAt is not null ||
            Status is not (OrderIntentStatus.Failed or OrderIntentStatus.Pending))
        {
            throw Invalid("Only an undelivered submit that may have left the process is confirmed absent, once.");
        }

        Status = OrderIntentStatus.Failed;
        NextAttemptAt = null;
        ClaimedAt = null;
        ConfirmedAbsentAt = now;
        ConfirmedAbsentBy = owner;
        AddDomainEvent(new OrderIntentAbsenceConfirmed(BotId, Id, EngineOrderId, owner, now));
    }

    /// <summary>
    ///     The owner confirms that a delivered submit, which the venue had accepted, was removed by the venue on its own
    ///     (an expired day order) and executed nothing. The intent stays delivered; the next reconciliation decides, and
    ///     only for the venue that checks it (ADR 0028).
    /// </summary>
    public void ConfirmExpired(UserId owner, DateTimeOffset now)
    {
        if (Kind != OrderIntentKind.Submit || PlaceStartedAt is null || ConfirmedAbsentAt is not null || Status != OrderIntentStatus.Sent)
        {
            throw Invalid("Only a delivered submit is confirmed expired, once.");
        }

        ConfirmedAbsentAt = now;
        ConfirmedAbsentBy = owner;
        AddDomainEvent(new OrderIntentAbsenceConfirmed(BotId, Id, EngineOrderId, owner, now));
    }

    /// <summary>
    ///     The gateway proved by itself that a delivered submit, which the venue had accepted, was removed by the venue and
    ///     executed nothing, and closes it without the owner (ADR 0030). The intent stays delivered; <paramref name="checks" />
    ///     are the codes of the evidence, recorded in the event for the bot's feed.
    /// </summary>
    public void ConfirmExpiredAutomatically(IReadOnlyList<string> checks, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(checks);

        if (Kind != OrderIntentKind.Submit || PlaceStartedAt is null || ConfirmedAbsentAt is not null || Status != OrderIntentStatus.Sent || checks.Count == 0)
        {
            throw Invalid("Only a delivered submit is closed automatically, once, with the checks that justify it.");
        }

        ConfirmedAbsentAt = now;
        ConfirmedAbsentAutomatically = true;
        AddDomainEvent(new OrderIntentAbsenceProven(BotId, Id, EngineOrderId, [.. checks], now));
    }

    /// <summary>
    ///     A failed submit that never left the process may be tried again from the start. Returns whether it was reset.
    /// </summary>
    public bool ResetUnsentFailure()
    {
        if (Status != OrderIntentStatus.Failed || PlaceStartedAt is not null)
        {
            return false;
        }

        Status = OrderIntentStatus.Pending;
        Attempts = 0;
        NextAttemptAt = null;

        return true;
    }

    public OrderIntentSnapshot ToSnapshot()
    {
        return new OrderIntentSnapshot(
            Id,
            BotId,
            EngineOrderId,
            Kind,
            ClientOrderId,
            Order,
            FromKill,
            Status,
            Attempts,
            NextAttemptAt,
            ClaimedAt,
            PlaceStartedAt,
            SentAt,
            Rejection,
            CreatedAt,
            ConfirmedAbsentAt,
            ConfirmedAbsentBy,
            Deferral,
            ConfirmedAbsentAutomatically);
    }

    private void RejectNow(OrderIntentRejection rejection, DateTimeOffset now)
    {
        Status = OrderIntentStatus.Rejected;
        Deferral = null;
        Rejection = rejection;
        SentAt = now;
        ClaimedAt = null;
        NextAttemptAt = null;
        AddDomainEvent(new OrderIntentRejected(BotId, Id, EngineOrderId, rejection.Reason, now));
    }

    private void EnsureClaimedSubmit()
    {
        if (Status != OrderIntentStatus.Sending || Kind != OrderIntentKind.Submit)
        {
            throw Invalid("The operation needs a claimed submit.");
        }
    }

    private static OrderIntentRejection KillRejection()
    {
        return new OrderIntentRejection(RejectionReason.KillSwitch, "A kill switch blocked the order before it was sent.");
    }

    private static BotRuleException Invalid(string message)
    {
        return new BotRuleException(BotRuleException.InvalidIntentTransition, message);
    }
}