namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Принятый симулированный ордер. Количество и лимитная цена уже нормализованы и не меняются до терминального состояния;
///     у рыночной покупки вместо количества зафиксирован бюджет, а количество определяется ценой исполнения.
///     Reservation — зарезервированная сумма: котируемая валюта для покупки, базовый актив для продажи.
/// </summary>
public sealed class Order
{
    public long Id { get; }
    public string? Tag { get; }
    public OrderSide Side { get; }
    public OrderType Type { get; }
    public decimal? Quantity { get; }
    public decimal? LimitPrice { get; }
    public decimal? QuoteBudget { get; }
    public decimal Reservation { get; private set; }
    public string OriginNodeId { get; }
    public int EligibleFromCandleIndex { get; }
    public OrderStatus Status { get; private set; }

    public bool IsActive => Status is OrderStatus.Active or OrderStatus.PendingSubmit or OrderStatus.PendingCancel;

    internal Order(
        long id,
        string? tag,
        OrderSide side,
        OrderType type,
        decimal? quantity,
        decimal? limitPrice,
        decimal? quoteBudget,
        decimal reservation,
        string originNodeId,
        int eligibleFromCandleIndex)
    {
        Id = id;
        Tag = tag;
        Side = side;
        Type = type;
        Quantity = quantity;
        LimitPrice = limitPrice;
        QuoteBudget = quoteBudget;
        Reservation = reservation;
        OriginNodeId = originNodeId;
        EligibleFromCandleIndex = eligibleFromCandleIndex;
        Status = OrderStatus.Active;
    }

    public bool IsEligibleAt(int candleIndex)
    {
        return IsActive && candleIndex >= EligibleFromCandleIndex;
    }

    internal void Close(OrderStatus status)
    {
        if (!IsActive || status is not (OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected))
        {
            throw new TradingInvariantException($"Order {Id} cannot move from {Status} to {status}.");
        }

        Status = status;
    }

    internal void AwaitSubmission()
    {
        if (Status != OrderStatus.Active)
        {
            throw new TradingInvariantException($"Order {Id} cannot await submission from {Status}.");
        }

        Status = OrderStatus.PendingSubmit;
    }

    internal void ConfirmSubmission()
    {
        if (Status == OrderStatus.PendingSubmit)
        {
            Status = OrderStatus.Active;
        }
        else if (Status != OrderStatus.PendingCancel)
        {
            throw new TradingInvariantException($"Order {Id} cannot confirm submission from {Status}.");
        }
    }

    internal void AwaitCancellation()
    {
        if (Status is not (OrderStatus.Active or OrderStatus.PendingSubmit))
        {
            throw new TradingInvariantException($"Order {Id} cannot await cancellation from {Status}.");
        }

        Status = OrderStatus.PendingCancel;
    }

    internal void ConsumeReservation(decimal amount)
    {
        if (!IsActive || amount < 0m || amount > Reservation)
        {
            throw new TradingInvariantException($"Order {Id} cannot consume {amount} from its reservation of {Reservation}.");
        }

        Reservation -= amount;
    }
}