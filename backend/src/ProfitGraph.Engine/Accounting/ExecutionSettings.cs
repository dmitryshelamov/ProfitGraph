namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Параметры прогона, влияющие на исполнение: стартовый капитал в котируемой валюте, проскальзывание рыночных ордеров
///     и комиссии. Комиссии — либо ставки лимитных и рыночных исполнений, либо комиссии продавца за единицу
///     (<see cref="SellerFeeSchedule" />, ADR 0022); со вторыми обе ставки равны нулю. Ставки и проскальзывание — доли в [0, 1).
/// </summary>
public sealed record ExecutionSettings
{
    public decimal StartingCapital { get; }
    public decimal Slippage { get; }
    public decimal LimitFeeRate { get; }
    public decimal MarketFeeRate { get; }

    /// <summary>
    ///     Комиссии продавца за единицу вместо ставок, или null.
    /// </summary>
    public SellerFeeSchedule? SellerFees { get; }

    /// <summary>
    ///     Время, через которое купленный актив можно продать (ADR 0022), или null. Действует только в replay.
    /// </summary>
    public TimeSpan? SettlementHold { get; private init; }

    /// <summary>
    ///     When a resting limit order fills in replay (ADR 0057); live bots never read it.
    /// </summary>
    public LimitFillMode LimitFill { get; private init; }

    /// <summary>
    ///     The leverage of a margin run (ADR 0061), or null for a spot run. A margin run reserves an opening order's notional
    ///     divided by it, plus the order's fee, and pays every fee in quote.
    /// </summary>
    public decimal? Leverage { get; private init; }

    /// <summary>
    ///     Which way the run trades; a short run is a margin run.
    /// </summary>
    public PositionDirection Direction { get; private init; }

    public bool IsMargin => Leverage.HasValue;

    /// <summary>
    ///     The side of an order that opens or adds to the position.
    /// </summary>
    public OrderSide OpeningSide => Direction == PositionDirection.Long ? OrderSide.Buy : OrderSide.Sell;

    public ExecutionSettings(decimal startingCapital, decimal slippage, decimal limitFeeRate, decimal marketFeeRate)
        : this(startingCapital, slippage, limitFeeRate, marketFeeRate, sellerFees: null)
    {
    }

    public ExecutionSettings(decimal startingCapital, decimal slippage, SellerFeeSchedule sellerFees)
        : this(startingCapital, slippage, limitFeeRate: 0m, marketFeeRate: 0m, sellerFees ?? throw new ArgumentNullException(nameof(sellerFees)))
    {
    }

    private ExecutionSettings(decimal startingCapital, decimal slippage, decimal limitFeeRate, decimal marketFeeRate, SellerFeeSchedule? sellerFees)
    {
        if (startingCapital <= 0m)
        {
            throw new TradingInvariantException("Starting capital must be positive.");
        }

        if (!IsFraction(slippage) || !IsFraction(limitFeeRate) || !IsFraction(marketFeeRate))
        {
            throw new TradingInvariantException("Slippage and fee rates must be in [0, 1).");
        }

        StartingCapital = startingCapital;
        Slippage = slippage;
        LimitFeeRate = limitFeeRate;
        MarketFeeRate = marketFeeRate;
        SellerFees = sellerFees;
    }

    /// <summary>
    ///     Те же параметры с задержкой продажи купленного актива.
    /// </summary>
    public ExecutionSettings WithSettlementHold(TimeSpan hold)
    {
        if (hold < TimeSpan.Zero)
        {
            throw new TradingInvariantException("A settlement hold cannot be negative.");
        }

        return this with { SettlementHold = hold };
    }

    /// <summary>
    ///     The same parameters with the given limit fill mode.
    /// </summary>
    public ExecutionSettings WithLimitFill(LimitFillMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new TradingInvariantException("Unknown limit fill mode.");
        }

        return this with { LimitFill = mode };
    }

    /// <summary>
    ///     The same parameters as a margin run with the given leverage (ADR 0061).
    /// </summary>
    public ExecutionSettings WithMargin(decimal leverage)
    {
        if (leverage < 1m)
        {
            throw new TradingInvariantException("Leverage must be at least 1.");
        }

        if (SellerFees is not null || SettlementHold is not null)
        {
            throw new TradingInvariantException("A margin run has neither seller fees nor a settlement hold.");
        }

        return this with { Leverage = leverage };
    }

    /// <summary>
    ///     The same parameters trading in the given direction; a short run without a leverage becomes a margin run at 1×.
    /// </summary>
    public ExecutionSettings WithDirection(PositionDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new TradingInvariantException("Unknown position direction.");
        }

        ExecutionSettings settings = direction == PositionDirection.Short && Leverage is null ? WithMargin(leverage: 1m) : this;

        return settings with { Direction = direction };
    }

    public decimal FeeRate(OrderType type)
    {
        return type == OrderType.Limit ? LimitFeeRate : MarketFeeRate;
    }

    private static bool IsFraction(decimal value)
    {
        return value is >= 0m and < 1m;
    }
}