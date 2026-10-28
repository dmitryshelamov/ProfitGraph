using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.OrderCancels;

/// <summary>
///     The owner's request to cancel one working order of a running bot (ADR 0048). The runtime that holds the bot's lease
///     resolves it as a root, in the same transaction as the checkpoint: the engine asks to cancel the order, so the cancel goes out
///     as an ordinary cancel intent, or the request is rejected with a stable code. A request not taken within
///     <see cref="Lifetime" /> expires, so a stopped bot never cancels it later.
/// </summary>
public sealed class OrderCancelRequest : AggregateRoot
{
    public const int MaxReasonLength = 64;

    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public OrderCancelRequestId Id { get; }
    public BotId BotId { get; }

    /// <summary>
    ///     The engine order the owner wants cancelled.
    /// </summary>
    public long EngineOrderId { get; }

    public OrderCancelStatus Status { get; private set; }

    /// <summary>
    ///     A stable code, such as <c>order_not_open</c> or <c>expired</c>.
    /// </summary>
    public string? RejectionReason { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    private OrderCancelRequest(OrderCancelRequestId id, BotId botId, long engineOrderId, DateTimeOffset createdAt)
    {
        if (engineOrderId <= 0)
        {
            throw new BotRuleException(BotRuleException.InvalidSettings, "An order cancel needs a positive engine order identifier.");
        }

        Id = id;
        BotId = botId;
        EngineOrderId = engineOrderId;
        Status = OrderCancelStatus.Pending;
        CreatedAt = createdAt;
    }

    public static OrderCancelRequest Create(OrderCancelRequestId id, BotId botId, long engineOrderId, DateTimeOffset now)
    {
        return new OrderCancelRequest(id, botId, engineOrderId, now);
    }

    public static OrderCancelRequest Restore(
        OrderCancelRequestId id,
        BotId botId,
        long engineOrderId,
        OrderCancelStatus status,
        string? rejectionReason,
        DateTimeOffset createdAt,
        DateTimeOffset? resolvedAt)
    {
        if (!Enum.IsDefined(status) || status == OrderCancelStatus.Pending != resolvedAt is null ||
            status == OrderCancelStatus.Rejected != rejectionReason is not null)
        {
            throw new ArgumentException("Invalid stored order cancel request.");
        }

        return new OrderCancelRequest(id, botId, engineOrderId, createdAt)
        {
            Status = status,
            RejectionReason = rejectionReason,
            ResolvedAt = resolvedAt
        };
    }

    public bool IsExpired(DateTimeOffset now)
    {
        return Status == OrderCancelStatus.Pending && CreatedAt < now - Lifetime;
    }

    /// <summary>
    ///     The engine asked to cancel the order; the cancel now follows the normal intent path and the bot's feed records it.
    /// </summary>
    public void MarkRequested(DateTimeOffset now)
    {
        EnsurePending();
        Status = OrderCancelStatus.Requested;
        ResolvedAt = now;
        AddDomainEvent(new OrderCancelRequested(BotId, Id, EngineOrderId, now));
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaxReasonLength)
        {
            throw new ArgumentException("A rejection needs a short reason code.", nameof(reason));
        }

        EnsurePending();
        Status = OrderCancelStatus.Rejected;
        RejectionReason = reason;
        ResolvedAt = now;
    }

    /// <summary>
    ///     Rejects the request as <c>expired</c> when nobody took it in time. Returns whether it expired.
    /// </summary>
    public bool Expire(DateTimeOffset now)
    {
        if (!IsExpired(now))
        {
            return false;
        }

        Reject("expired", now);

        return true;
    }

    private void EnsurePending()
    {
        if (Status != OrderCancelStatus.Pending)
        {
            throw new BotRuleException(BotRuleException.OrderCancelResolved, "The order cancel request is already resolved.");
        }
    }
}