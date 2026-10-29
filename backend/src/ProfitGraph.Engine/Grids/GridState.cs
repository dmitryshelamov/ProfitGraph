using System.Collections.Immutable;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Grids;

/// <summary>
///     One grid's levels and lifecycle (ADR 0058). It books its own orders' outcomes into its levels, decides the stop and the
///     upward shift at a candle close, and plans the submits and cancels that turn the book into the orders it wants. It never
///     touches the account itself, so replay and live bots apply its plan the same way.
/// </summary>
internal sealed class GridState
{
    private readonly SortedDictionary<int, GridLevel> _levels = new SortedDictionary<int, GridLevel>();

    public string NodeId { get; }
    public GridPhase Phase { get; private set; }
    public decimal? Anchor { get; private set; }
    public decimal? FirstAnchor { get; private set; }
    public bool SellOnStop { get; private set; }
    public bool StopSubmitted { get; private set; }

    public GridState(string nodeId)
    {
        NodeId = nodeId;
    }

    public static GridState Restore(GridCheckpoint checkpoint)
    {
        var state = new GridState(checkpoint.NodeId)
        {
            Phase = checkpoint.Phase,
            Anchor = checkpoint.Anchor,
            FirstAnchor = checkpoint.FirstAnchor,
            SellOnStop = checkpoint.SellOnStop,
            StopSubmitted = checkpoint.StopSubmitted
        };

        foreach (GridLevelCheckpoint level in checkpoint.Levels)
        {
            state._levels[level.Index] = new GridLevel { HeldBase = level.HeldBase, PaidQuote = level.PaidQuote, Blocked = level.Blocked };
        }

        return state;
    }

    public GridCheckpoint Capture()
    {
        return new GridCheckpoint(
            NodeId,
            Phase,
            Anchor,
            FirstAnchor,
            SellOnStop,
            StopSubmitted,
            _levels.Where(pair => !pair.Value.IsEmpty)
                .Select(pair => new GridLevelCheckpoint(pair.Key, pair.Value.HeldBase, pair.Value.PaidQuote, pair.Value.Blocked))
                .ToImmutableArray());
    }

    /// <summary>
    ///     Starts a grid that does not run, anchored at <paramref name="price" />; a running or stopping grid ignores it.
    /// </summary>
    public bool Start(decimal price)
    {
        if (Phase != GridPhase.Stopped || price <= 0m)
        {
            return false;
        }

        Phase = GridPhase.Running;
        Anchor = price;
        FirstAnchor = price;
        SellOnStop = false;
        StopSubmitted = false;
        _levels.Clear();

        return true;
    }

    /// <summary>
    ///     Begins a stop: every grid order is cancelled and, with <paramref name="sell" />, the levels' base is then sold at market.
    /// </summary>
    public void RequestStop(bool sell)
    {
        if (Phase != GridPhase.Running)
        {
            return;
        }

        Phase = GridPhase.Stopping;
        SellOnStop = sell;
    }

    /// <summary>
    ///     Books one outcome of the grid's own order into its level. Returns true when the stop's market sale ended the grid.
    /// </summary>
    public bool Book(EventOccurrence occurrence, IGraphAccount account)
    {
        (string? tag, long? orderId) = occurrence.Payload switch
        {
            FillPayload fill => (fill.Fill.Tag, fill.Fill.OrderId),
            OrderPayload order => (order.Order.Tag, order.Order.Id),
            RejectionPayload rejection => (rejection.Rejection.Tag, rejection.Rejection.OrderId),
            _ => ((string?)null, (long?)null)
        };

        if (occurrence.Kind is not (EventKind.Filled or EventKind.Cancelled or EventKind.Rejected) ||
            !GridTags.TryParse(NodeId, tag, out int index, out string role))
        {
            return false;
        }

        if (role == GridTags.StopRole)
        {
            if (Phase != GridPhase.Stopping)
            {
                return false;
            }

            Finish();

            return true;
        }

        GridLevel level = Level(index);

        if (occurrence.Kind == EventKind.Rejected)
        {
            level.Blocked = true;

            return false;
        }

        OrderExecutedTotals totals = occurrence.Payload is FillPayload filled
            ? new OrderExecutedTotals(filled.Fill.GrossBase, filled.Fill.BaseChange, filled.Fill.QuoteChange)
            : orderId is { } id
                ? account.ExecutedTotals(id)
                : OrderExecutedTotals.None;

        if (role == GridTags.BuyRole)
        {
            level.HeldBase += totals.BaseChange;
            level.PaidQuote -= totals.QuoteChange;
        }
        else if (occurrence.Kind == EventKind.Filled || totals.GrossBase >= level.HeldBase)
        {
            // A completed sale frees the level; what rounding left over stays in the account, outside the grid.
            level.HeldBase = 0m;
            level.PaidQuote = 0m;
        }
        else if (totals.GrossBase > 0m)
        {
            level.PaidQuote = level.PaidQuote * (level.HeldBase - totals.GrossBase) / level.HeldBase;
            level.HeldBase -= totals.GrossBase;
        }

        return false;
    }

    /// <summary>
    ///     At a candle close: rejected levels may try again, and the stop below the grid or the upward shift is decided.
    /// </summary>
    public void OnClose(GridSettings settings, decimal close, IGraphAccount account, AccountSnapshot snapshot)
    {
        if (Phase != GridPhase.Running)
        {
            return;
        }

        foreach (GridLevel level in _levels.Values)
        {
            level.Blocked = false;
        }

        decimal anchor = Anchor!.Value;

        if (settings.StopBelow > 0m && close < settings.LevelPrice(anchor, settings.Levels) * (1m - settings.StopBelow))
        {
            RequestStop(sell: true);

            return;
        }

        bool holdsNothing = _levels.Values.All(level => level.HeldBase <= 0m) &&
            snapshot.Orders.All(order => !IsOwn(order) || account.ExecutedTotals(order.Id).GrossBase == 0m);

        if (settings.ShiftThreshold > 0m && holdsNothing && close > anchor * (1m + settings.ShiftThreshold) &&
            close <= FirstAnchor!.Value * (1m + settings.AnchorCeiling))
        {
            Anchor = close;
        }
    }

    /// <summary>
    ///     The cancels and submits that turn the grid's active orders into the ones it wants now (section 4), in level order.
    ///     <paramref name="stopped" /> is true when a stop without a sale, or with nothing to sell, ended the grid.
    /// </summary>
    public IReadOnlyList<ActionRequest> Plan(
        GridSettings? settings,
        IGraphAccount account,
        AccountSnapshot snapshot,
        ReachedMarketContext market,
        out bool stopped)
    {
        stopped = false;
        var requests = new List<ActionRequest>();
        OrderSnapshot[] own = snapshot.Orders.Where(IsOwn).OrderBy(order => order.Tag, StringComparer.Ordinal).ToArray();

        if (Phase == GridPhase.Stopping)
        {
            foreach (OrderSnapshot order in own)
            {
                if (order.Status != OrderStatus.PendingCancel && order.Tag != GridTags.Stop(NodeId))
                {
                    requests.Add(CancelActionRequest.ByOrderId(NodeId, order.Id));
                }
            }

            if (own.Length > 0 || StopSubmitted)
            {
                return requests;
            }

            decimal held = _levels.Values.Sum(level => Math.Max(level.HeldBase, val2: 0m));
            decimal quantity = OrderNormalizer.NormalizeQuantity(Math.Min(held, snapshot.BaseAvailable), account.Rules);

            if (SellOnStop && OrderNormalizer.CheckMinimums(quantity, price: null, account.Rules) is null)
            {
                StopSubmitted = true;
                requests.Add(
                    new SubmitActionRequest(
                        new OrderRequest(
                            OrderSide.Sell,
                            OrderType.Market,
                            quantity,
                            LimitPrice: null,
                            GridTags.Stop(NodeId),
                            NodeId,
                            market.EligibleFromCandleIndex)));

                return requests;
            }

            Finish();
            stopped = true;

            return requests;
        }

        if (Phase != GridPhase.Running || settings is null)
        {
            return requests;
        }

        // Wanted orders in priority order: sales by level, then buys from the highest price down, so scarce quote goes to the highest price.
        var wanted = new Dictionary<string, (OrderSide Side, decimal Size, decimal Price)>(StringComparer.Ordinal);
        var priority = new List<string>();
        var buying = new HashSet<int>();
        var keptPartialBuys = 0;

        foreach (OrderSnapshot order in own)
        {
            if (GridTags.TryParse(NodeId, order.Tag, out int index, out string role) && role == GridTags.BuyRole &&
                account.ExecutedTotals(order.Id).GrossBase > 0m)
            {
                // A buy that already bought part of its lot keeps working; its level sells once it ends.
                wanted[order.Tag!] = (OrderSide.Buy, order.QuoteBudget ?? 0m, order.LimitPrice ?? 0m);
                buying.Add(index);
                keptPartialBuys++;
            }
        }

        foreach ((int index, GridLevel level) in _levels)
        {
            if (level.HeldBase <= 0m || buying.Contains(index))
            {
                continue;
            }

            decimal quantity = OrderNormalizer.NormalizeQuantity(level.HeldBase, account.Rules);
            decimal price = CeilToTick(level.PaidQuote / level.HeldBase * (1m + settings.ProfitStep), account.Rules.PriceTick);

            if (OrderNormalizer.CheckMinimums(quantity, price, account.Rules) is null)
            {
                wanted[GridTags.Sell(NodeId, index)] = (OrderSide.Sell, quantity, price);
                priority.Add(GridTags.Sell(NodeId, index));
            }
        }

        int openBuys = keptPartialBuys;
        decimal anchor = Anchor!.Value;
        decimal available = snapshot.QuoteAvailable;
        decimal reserve = settings.Lot * (1m + account.Settings.LimitFeeRate);

        for (var index = 1; index <= settings.Levels && openBuys < settings.MaxOpenBuys; index++)
        {
            GridLevel? level = _levels.GetValueOrDefault(index);

            if (buying.Contains(index) || level is { HeldBase: > 0m } || level is { Blocked: true })
            {
                continue;
            }

            decimal price = OrderNormalizer.NormalizePrice(settings.LevelPrice(anchor, index), account.Rules);

            if (price <= 0m || price >= market.MarketPrice)
            {
                continue;
            }

            wanted[GridTags.Buy(NodeId, index)] = (OrderSide.Buy, settings.Lot, price);
            priority.Add(GridTags.Buy(NodeId, index));
            openBuys++;
        }

        var occupied = new HashSet<string>(StringComparer.Ordinal);

        foreach (OrderSnapshot order in own)
        {
            occupied.Add(order.Tag!);

            if (order.Status == OrderStatus.PendingCancel)
            {
                continue;
            }

            bool keep = wanted.TryGetValue(order.Tag!, out (OrderSide Side, decimal Size, decimal Price) want) && want.Price == order.LimitPrice &&
                (want.Side == OrderSide.Buy || want.Size == order.Quantity);

            if (!keep)
            {
                requests.Add(CancelActionRequest.ByOrderId(NodeId, order.Id));
            }
        }

        foreach (string tag in priority)
        {
            (OrderSide side, decimal size, decimal price) = wanted[tag];

            if (occupied.Contains(tag))
            {
                continue;
            }

            if (side == OrderSide.Buy)
            {
                if (available < reserve)
                {
                    continue;
                }

                available -= reserve;
            }

            requests.Add(new SubmitActionRequest(new OrderRequest(side, OrderType.Limit, size, price, tag, NodeId, market.EligibleFromCandleIndex)));
        }

        return requests;
    }

    private static decimal CeilToTick(decimal value, decimal tick)
    {
        return decimal.Ceiling(value / tick) * tick;
    }

    private bool IsOwn(OrderSnapshot order)
    {
        return order.OriginNodeId == NodeId && order.Tag is not null && order.Tag.StartsWith(GridTags.Prefix(NodeId), StringComparison.Ordinal);
    }

    private GridLevel Level(int index)
    {
        if (!_levels.TryGetValue(index, out GridLevel? level))
        {
            level = new GridLevel();
            _levels[index] = level;
        }

        return level;
    }

    private void Finish()
    {
        Phase = GridPhase.Stopped;
        Anchor = null;
        FirstAnchor = null;
        SellOnStop = false;
        StopSubmitted = false;
        _levels.Clear();
    }
}