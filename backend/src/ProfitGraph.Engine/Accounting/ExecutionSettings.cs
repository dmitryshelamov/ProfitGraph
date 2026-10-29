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

    public decimal FeeRate(OrderType type)
    {
        return type == OrderType.Limit ? LimitFeeRate : MarketFeeRate;
    }

    private static bool IsFraction(decimal value)
    {
        return value is >= 0m and < 1m;
    }
}