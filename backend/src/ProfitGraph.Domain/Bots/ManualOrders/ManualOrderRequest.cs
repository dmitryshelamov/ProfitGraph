using ProfitGraph.Domain.Common;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Domain.Bots.ManualOrders;

/// <summary>
///     An owner's order for a running bot (Profit Fox Designer ADR 0091, ADR 0046): a market or a limit order, a buy sized in
///     quote, a sell in base; a limit order also carries its price. The
///     runtime that holds the bot's lease resolves it as a root, in the same transaction as the checkpoint. A request not taken
///     within <see cref="Lifetime" /> expires, so a stopped bot never trades it later.
/// </summary>
public sealed class ManualOrderRequest : AggregateRoot
{
    public const int MaxReasonLength = 64;

    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public ManualOrderRequestId Id { get; }
    public BotId BotId { get; }
    public OrderSide Side { get; }
    public OrderType Type { get; }
    public decimal Amount { get; }

    /// <summary>
    ///     The price the owner asked for, as typed: the engine rounds it down to the instrument's tick. Set exactly for a limit order.
    /// </summary>
    public decimal? LimitPrice { get; }

    public ManualOrderStatus Status { get; private set; }
    public long? EngineOrderId { get; private set; }

    /// <summary>
    ///     A stable code, such as an engine rejection reason or <c>expired</c>.
    /// </summary>
    public string? RejectionReason { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    private ManualOrderRequest(
        ManualOrderRequestId id,
        BotId botId,
        OrderSide side,
        OrderType type,
        decimal amount,
        decimal? limitPrice,
        DateTimeOffset createdAt)
    {
        if (!Enum.IsDefined(side) || !Enum.IsDefined(type) || amount <= 0m)
        {
            throw new BotRuleException(BotRuleException.InvalidSettings, "A manual order needs a side, a type and a positive amount.");
        }

        if (type == OrderType.Limit != limitPrice.HasValue || limitPrice <= 0m)
        {
            throw new BotRuleException(BotRuleException.InvalidSettings, "A limit manual order needs a positive price, and a market order has none.");
        }

        Id = id;
        BotId = botId;
        Side = side;
        Type = type;
        Amount = amount;
        LimitPrice = limitPrice;
        Status = ManualOrderStatus.Pending;
        CreatedAt = createdAt;
    }

    public static ManualOrderRequest Create(ManualOrderRequestId id, BotId botId, OrderSide side, decimal amount, DateTimeOffset now)
    {
        return new ManualOrderRequest(id, botId, side, OrderType.Market, amount, limitPrice: null, now);
    }

    public static ManualOrderRequest CreateLimit(ManualOrderRequestId id, BotId botId, OrderSide side, decimal amount, decimal limitPrice, DateTimeOffset now)
    {
        return new ManualOrderRequest(id, botId, side, OrderType.Limit, amount, limitPrice, now);
    }

    public static ManualOrderRequest Restore(
        ManualOrderRequestId id,
        BotId botId,
        OrderSide side,
        OrderType type,
        decimal amount,
        decimal? limitPrice,
        ManualOrderStatus status,
        long? engineOrderId,
        string? rejectionReason,
        DateTimeOffset createdAt,
        DateTimeOffset? resolvedAt)
    {
        if (!Enum.IsDefined(status) || status == ManualOrderStatus.Pending != resolvedAt is null ||
            status == ManualOrderStatus.Submitted != engineOrderId is not null || status == ManualOrderStatus.Rejected != rejectionReason is not null)
        {
            throw new ArgumentException("Invalid stored manual order request.");
        }

        return new ManualOrderRequest(id, botId, side, type, amount, limitPrice, createdAt)
        {
            Status = status,
            EngineOrderId = engineOrderId,
            RejectionReason = rejectionReason,
            ResolvedAt = resolvedAt
        };
    }

    public bool IsExpired(DateTimeOffset now)
    {
        return Status == ManualOrderStatus.Pending && CreatedAt < now - Lifetime;
    }

    /// <summary>
    ///     The engine accepted the order under <paramref name="engineOrderId" />; it now follows the normal intent path.
    /// </summary>
    public void MarkSubmitted(long engineOrderId, DateTimeOffset now)
    {
        if (engineOrderId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(engineOrderId), "An engine order identifier is positive.");
        }

        EnsurePending();
        Status = ManualOrderStatus.Submitted;
        EngineOrderId = engineOrderId;
        ResolvedAt = now;
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaxReasonLength)
        {
            throw new ArgumentException("A rejection needs a short reason code.", nameof(reason));
        }

        EnsurePending();
        Status = ManualOrderStatus.Rejected;
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
        if (Status != ManualOrderStatus.Pending)
        {
            throw new BotRuleException(BotRuleException.ManualOrderResolved, "The manual order request is already resolved.");
        }
    }
}