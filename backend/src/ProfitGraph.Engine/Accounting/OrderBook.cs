namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Ордера одного прогона. Идентификаторы — возрастающая последовательность, порядок создания стабилен;
///     непустой тег занимает не больше одного активного ордера, терминальные ордера тег освобождают.
/// </summary>
public sealed class OrderBook
{
    private readonly List<Order> _active = [];
    private readonly Dictionary<string, Order> _activeByTag = new Dictionary<string, Order>(StringComparer.Ordinal);
    private readonly Dictionary<long, Order> _orders = [];

    /// <summary>
    ///     Активные ордера в порядке создания.
    /// </summary>
    public IReadOnlyList<Order> Active => _active;

    internal long LastIssuedId { get; private set; }

    public Order? Get(long id)
    {
        return _orders.GetValueOrDefault(id);
    }

    public Order? GetActive(string tag)
    {
        return _activeByTag.GetValueOrDefault(tag);
    }

    internal long NextId()
    {
        return ++LastIssuedId;
    }

    internal void Add(Order order)
    {
        if (order.Tag is not null && !_activeByTag.TryAdd(order.Tag, order))
        {
            throw new TradingInvariantException($"Tag '{order.Tag}' is already active.");
        }

        _orders.Add(order.Id, order);
        _active.Add(order);
    }

    internal void Restore(long nextOrderId, IReadOnlyList<OrderSnapshot> snapshots)
    {
        if (LastIssuedId != 0 || _active.Count != 0 || nextOrderId < 0 || snapshots.Any(snapshot => snapshot.Id > nextOrderId))
        {
            throw new ArgumentException("Invalid order-book checkpoint.", nameof(snapshots));
        }

        foreach (OrderSnapshot snapshot in snapshots.OrderBy(snapshot => snapshot.Id))
        {
            var order = new Order(
                snapshot.Id,
                snapshot.Tag,
                snapshot.Side,
                snapshot.Type,
                snapshot.Quantity,
                snapshot.LimitPrice,
                snapshot.QuoteBudget,
                snapshot.Reservation,
                snapshot.OriginNodeId,
                snapshot.EligibleFromCandleIndex);

            if (snapshot.Status == OrderStatus.PendingSubmit)
            {
                order.AwaitSubmission();
            }
            else if (snapshot.Status == OrderStatus.PendingCancel)
            {
                order.AwaitCancellation();
            }

            Add(order);
        }

        LastIssuedId = nextOrderId;
    }

    internal void Remove(Order order)
    {
        _active.Remove(order);

        if (order.Tag is not null)
        {
            _activeByTag.Remove(order.Tag);
        }
    }
}