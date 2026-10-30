using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.PositionCloses;

/// <summary>
///     The owner's close of a running bot's position (ADR 0064): the runner cancels the bot's working orders, sells what the
///     position holds at the market and, when asked or when the close fails, stops the bot. A request not taken within
///     <see cref="Lifetime" /> fails as expired, so a stopped bot never closes later.
/// </summary>
public sealed class BotPositionClose : AggregateRoot
{
    public const int MaxReasonLength = 64;
    public const string ExpiredReason = "expired";

    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public BotPositionCloseId Id { get; }
    public BotId BotId { get; }

    /// <summary>
    ///     Whether the bot stops once the position is closed; a failed close stops it anyway.
    /// </summary>
    public bool StopAfter { get; }

    public BotPositionCloseStatus Status { get; private set; }

    /// <summary>
    ///     How many working orders the close asked to cancel; set once it started.
    /// </summary>
    public int? CancelledOrders { get; private set; }

    /// <summary>
    ///     The engine order that sells the position; set once it was submitted.
    /// </summary>
    public long? EngineOrderId { get; private set; }

    /// <summary>
    ///     A stable code: an engine rejection reason of the sell, <c>expired</c>, or why the sale did not empty the position.
    /// </summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsActive => Status is BotPositionCloseStatus.Pending or BotPositionCloseStatus.Cancelling or BotPositionCloseStatus.Selling;

    private BotPositionClose(BotPositionCloseId id, BotId botId, bool stopAfter, DateTimeOffset createdAt)
    {
        Id = id;
        BotId = botId;
        StopAfter = stopAfter;
        Status = BotPositionCloseStatus.Pending;
        CreatedAt = createdAt;
    }

    public static BotPositionClose Create(BotPositionCloseId id, BotId botId, bool stopAfter, DateTimeOffset now)
    {
        return new BotPositionClose(id, botId, stopAfter, now);
    }

    public static BotPositionClose Restore(
        BotPositionCloseId id,
        BotId botId,
        bool stopAfter,
        BotPositionCloseStatus status,
        int? cancelledOrders,
        long? engineOrderId,
        string? failureReason,
        DateTimeOffset createdAt,
        DateTimeOffset? resolvedAt)
    {
        var close = new BotPositionClose(id, botId, stopAfter, createdAt)
        {
            Status = status,
            CancelledOrders = cancelledOrders,
            EngineOrderId = engineOrderId,
            FailureReason = failureReason,
            ResolvedAt = resolvedAt
        };

        if (!Enum.IsDefined(status) || close.IsActive != resolvedAt is null || status == BotPositionCloseStatus.Failed != failureReason is not null ||
            (status == BotPositionCloseStatus.Selling && engineOrderId is null) ||
            (status != BotPositionCloseStatus.Pending && cancelledOrders is null && status != BotPositionCloseStatus.Failed))
        {
            throw new ArgumentException("Invalid stored position close.");
        }

        return close;
    }

    public bool IsExpired(DateTimeOffset now)
    {
        return Status == BotPositionCloseStatus.Pending && CreatedAt < now - Lifetime;
    }

    /// <summary>
    ///     The runner took the close: the engine asked <paramref name="cancelledOrders" /> working orders to cancel and rejects the
    ///     strategy's orders until the close ends.
    /// </summary>
    public void Start(int cancelledOrders, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cancelledOrders);
        Ensure(BotPositionCloseStatus.Pending);
        Status = BotPositionCloseStatus.Cancelling;
        CancelledOrders = cancelledOrders;
        AddDomainEvent(new BotPositionCloseStarted(BotId, Id, cancelledOrders, StopAfter, now));
    }

    /// <summary>
    ///     No working order is left and the engine submitted the market sale of the position under <paramref name="engineOrderId" />.
    /// </summary>
    public void Sell(long engineOrderId, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(engineOrderId);
        Ensure(BotPositionCloseStatus.Cancelling);
        Status = BotPositionCloseStatus.Selling;
        EngineOrderId = engineOrderId;
    }

    /// <summary>
    ///     The position is closed: the sale ended it, or what is left cannot be sold.
    /// </summary>
    public void Close(DateTimeOffset now)
    {
        if (Status is not (BotPositionCloseStatus.Cancelling or BotPositionCloseStatus.Selling))
        {
            throw new BotRuleException(BotRuleException.PositionCloseResolved, "Only a started position close can end closed.");
        }

        Status = BotPositionCloseStatus.Closed;
        ResolvedAt = now;
        AddDomainEvent(new BotPositionCloseEnded(BotId, Id, Status, Reason: null, now));
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaxReasonLength)
        {
            throw new ArgumentException("A failed close needs a short reason code.", nameof(reason));
        }

        if (!IsActive)
        {
            throw new BotRuleException(BotRuleException.PositionCloseResolved, "The position close has already ended.");
        }

        Status = BotPositionCloseStatus.Failed;
        FailureReason = reason;
        ResolvedAt = now;
        AddDomainEvent(new BotPositionCloseEnded(BotId, Id, Status, reason, now));
    }

    /// <summary>
    ///     Fails the close as <c>expired</c> when nobody took it in time. Returns whether it expired.
    /// </summary>
    public bool Expire(DateTimeOffset now)
    {
        if (!IsExpired(now))
        {
            return false;
        }

        Fail(ExpiredReason, now);

        return true;
    }

    private void Ensure(BotPositionCloseStatus status)
    {
        if (Status != status)
        {
            throw new BotRuleException(BotRuleException.PositionCloseResolved, $"The position close is {Status}, not {status}.");
        }
    }
}