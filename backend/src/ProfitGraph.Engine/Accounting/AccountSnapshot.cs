using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Immutable committed account state shared by every value read in one occurrence dispatch.
/// </summary>
public sealed class AccountSnapshot
{
    private readonly decimal _pendingFeeReserve;
    public decimal StartingCapital { get; }
    public decimal QuoteTotal { get; }
    public decimal QuoteReserved { get; }
    public decimal QuoteAvailable => QuoteTotal - QuoteReserved - _pendingFeeReserve;
    public decimal BaseTotal { get; }
    public decimal BaseReserved { get; }
    public decimal BaseAvailable => BaseTotal - BaseReserved;
    public decimal CostBasis { get; }
    public decimal RealizedPnl { get; }
    public decimal FeesBase { get; }
    public decimal FeesQuote { get; }
    public decimal? LastBuyPrice { get; }
    public IReadOnlyList<OrderSnapshot> Orders { get; }

    private AccountSnapshot(TradingAccount account)
    {
        Portfolio portfolio = account.Portfolio;
        StartingCapital = portfolio.StartingCapital;
        QuoteTotal = portfolio.QuoteTotal;
        QuoteReserved = portfolio.QuoteReserved;
        _pendingFeeReserve = portfolio.PendingFeeReserve;
        BaseTotal = portfolio.BaseTotal;
        BaseReserved = portfolio.BaseReserved;
        CostBasis = portfolio.CostBasis;
        RealizedPnl = portfolio.RealizedPnl;
        FeesBase = portfolio.FeesBase;
        FeesQuote = portfolio.FeesQuote;
        LastBuyPrice = portfolio.LastBuyPrice;
        IReadOnlyList<Order> active = account.Orders.Active;
        var orders = new OrderSnapshot[active.Count];

        for (var index = 0; index < active.Count; index++)
        {
            orders[index] = OrderSnapshot.Capture(active[index]);
        }

        Orders = Array.AsReadOnly(orders);
    }

    private AccountSnapshot(
        decimal startingCapital,
        decimal quoteTotal,
        decimal quoteReserved,
        decimal baseTotal,
        decimal baseReserved,
        decimal costBasis,
        decimal realizedPnl,
        decimal feesBase,
        decimal feesQuote,
        decimal? lastBuyPrice,
        IEnumerable<OrderSnapshot> orders,
        bool allowPending,
        decimal pendingFeeReserve = 0m,
        bool allowQuoteShortfall = false)
    {
        OrderSnapshot[] copied = orders.ToArray();

        if (startingCapital <= 0m || (!allowQuoteShortfall && (quoteTotal < 0m || quoteReserved > quoteTotal)) || quoteReserved < 0m || pendingFeeReserve < 0m ||
            baseTotal < 0m || baseReserved < 0m || baseReserved > baseTotal || costBasis < 0m ||
            feesBase < 0m || feesQuote < 0m || lastBuyPrice <= 0m ||
            copied.Any(order => order is null || order.Id <= 0 || order.Reservation < 0m || order.EligibleFromCandleIndex < 0 ||
                (order.Status != OrderStatus.Active && (!allowPending || order.Status is not (OrderStatus.PendingSubmit or OrderStatus.PendingCancel))) ||
                !Enum.IsDefined(order.Side) || !Enum.IsDefined(order.Type) ||
                string.IsNullOrWhiteSpace(order.OriginNodeId)) || copied.Select(order => order.Id).Distinct().Count() != copied.Length)
        {
            throw new BacktestRunInvariantException("Invalid persisted account snapshot.");
        }

        StartingCapital = startingCapital;
        QuoteTotal = quoteTotal;
        QuoteReserved = quoteReserved;
        _pendingFeeReserve = pendingFeeReserve;
        BaseTotal = baseTotal;
        BaseReserved = baseReserved;
        CostBasis = costBasis;
        RealizedPnl = realizedPnl;
        FeesBase = feesBase;
        FeesQuote = feesQuote;
        LastBuyPrice = lastBuyPrice;
        Orders = Array.AsReadOnly(copied);
    }

    /// <summary>
    ///     Restores committed immutable values without replaying trading mutations or recomputing financial marks.
    /// </summary>
    public static AccountSnapshot Restore(
        decimal startingCapital,
        decimal quoteTotal,
        decimal quoteReserved,
        decimal baseTotal,
        decimal baseReserved,
        decimal costBasis,
        decimal realizedPnl,
        decimal feesBase,
        decimal feesQuote,
        decimal? lastBuyPrice,
        IEnumerable<OrderSnapshot> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        return new AccountSnapshot(
            startingCapital,
            quoteTotal,
            quoteReserved,
            baseTotal,
            baseReserved,
            costBasis,
            realizedPnl,
            feesBase,
            feesQuote,
            lastBuyPrice,
            orders,
            allowPending: false);
    }

    internal static AccountSnapshot RestoreForExternal(
        decimal startingCapital,
        decimal quoteTotal,
        decimal quoteReserved,
        decimal baseTotal,
        decimal baseReserved,
        decimal costBasis,
        decimal realizedPnl,
        decimal feesBase,
        decimal feesQuote,
        decimal? lastBuyPrice,
        IEnumerable<OrderSnapshot> orders,
        decimal pendingFeeReserve = 0m,
        bool allowQuoteShortfall = false)
    {
        ArgumentNullException.ThrowIfNull(orders);

        return new AccountSnapshot(
            startingCapital,
            quoteTotal,
            quoteReserved,
            baseTotal,
            baseReserved,
            costBasis,
            realizedPnl,
            feesBase,
            feesQuote,
            lastBuyPrice,
            orders,
            allowPending: true,
            pendingFeeReserve,
            allowQuoteShortfall);
    }

    public static AccountSnapshot Capture(TradingAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new AccountSnapshot(account);
    }
}