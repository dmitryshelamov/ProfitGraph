namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Балансы одного прогона: итоговые и зарезервированные суммы обоих активов, средневзвешенная стоимость позиции,
///     реализованный результат и удержанные комиссии. Equity включает резервы, поэтому выставление ордера не меняет PnL.
///     Стоимость позиции не округляется к шагу количества; пыль остаётся в позиции и в equity.
///     A margin run (ADR 0061) keeps the same cash view: a short holds a negative base balance and its proceeds in quote, so equity
///     stays quote + base × close; what may still open is the quote less the margin of the position at its entry notional.
/// </summary>
public sealed class Portfolio
{
    private bool _externalQuoteShortfallAllowed;
    private decimal? _leverage;
    private PositionDirection _direction;
    internal decimal PendingFeeReserve { get; private set; }
    public decimal StartingCapital { get; private set; }
    public decimal QuoteTotal { get; private set; }
    public decimal QuoteReserved { get; private set; }
    public decimal BaseTotal { get; private set; }
    public decimal BaseReserved { get; private set; }
    public decimal CostBasis { get; private set; }
    public decimal RealizedPnl { get; private set; }
    public decimal FeesBase { get; private set; }
    public decimal FeesQuote { get; private set; }
    public decimal? LastBuyPrice { get; private set; }

    /// <summary>
    ///     The entry fees the position's cost basis holds, in quote: a fee paid in base valued at its fill's price, a fee paid in
    ///     quote as it is. It leaves with the cost basis in proportion to what is sold or covered. Strategies and the realized
    ///     profit keep the fee in the cost; reports subtract it to show an entry fee as spent when it is paid (ADR 0063).
    /// </summary>
    public decimal FeesInCost { get; private set; }

    /// <summary>
    ///     The notional of a margin run's position at its entry prices, without fees; zero in a spot run.
    /// </summary>
    public decimal EntryNotional { get; private set; }

    public decimal QuoteAvailable => QuoteTotal - QuoteReserved - PendingFeeReserve + MarginRelease;

    /// <summary>
    ///     The position quantity free to close: the base a long holds or the base a short owes, less what working exits reserve.
    /// </summary>
    public decimal BaseAvailable => Math.Abs(BaseTotal) - BaseReserved;

    /// <summary>
    ///     What a margin run's open position adds to the quote that may open more: the entry notional a long paid or a short received
    ///     back out of the cash view, less the position's margin. Zero in a spot run and when flat.
    /// </summary>
    internal decimal MarginRelease => _leverage is { } leverage ? (BaseTotal >= 0m ? EntryNotional : -EntryNotional) - EntryNotional / leverage : 0m;

    internal Portfolio(decimal startingCapital)
    {
        StartingCapital = startingCapital;
        QuoteTotal = startingCapital;
    }

    internal void Restore(ExternalAccountCheckpoint checkpoint)
    {
        if (checkpoint.StartingCapital != StartingCapital)
        {
            throw new ArgumentException("Checkpoint capital does not match execution settings.", nameof(checkpoint));
        }

        QuoteTotal = checkpoint.QuoteTotal;
        QuoteReserved = checkpoint.QuoteReserved;
        BaseTotal = checkpoint.BaseTotal;
        BaseReserved = checkpoint.BaseReserved;
        CostBasis = checkpoint.CostBasis;
        RealizedPnl = checkpoint.RealizedPnl;
        FeesBase = checkpoint.FeesBase;
        FeesQuote = checkpoint.FeesQuote;
        LastBuyPrice = checkpoint.LastBuyPrice;
        EntryNotional = checkpoint.EntryNotional;
        FeesInCost = checkpoint.FeesInCost;
        EnsureInvariants();
    }

    /// <summary>
    ///     Makes the portfolio a margin run's (ADR 0061); only before anything is traded or restored.
    /// </summary>
    internal void ConfigureMargin(decimal leverage, PositionDirection direction)
    {
        _leverage = leverage;
        _direction = direction;
        _externalQuoteShortfallAllowed = true;
    }

    public decimal Equity(decimal close)
    {
        return QuoteTotal + BaseTotal * close;
    }

    public decimal UnrealizedPnl(decimal close)
    {
        return BaseTotal >= 0m ? BaseTotal * close - CostBasis : BaseTotal * close + CostBasis;
    }

    /// <summary>
    ///     Whether the account's money can become <paramref name="newCapital" />: what the capital changes is free quote, so it
    ///     must stay at least what is reserved.
    /// </summary>
    internal bool CanAdjustCapital(decimal newCapital)
    {
        return newCapital > 0m && QuoteTotal + newCapital - StartingCapital - QuoteReserved - PendingFeeReserve >= 0m;
    }

    /// <summary>
    ///     Gives the account a new starting capital while it runs (ADR 0025): the difference is added to, or taken from, the quote
    ///     balance. Cost basis, realized profit, fees and fills are untouched.
    /// </summary>
    internal void AdjustCapital(decimal newCapital)
    {
        if (!CanAdjustCapital(newCapital))
        {
            throw new TradingInvariantException($"The capital {newCapital} leaves less quote than is reserved ({QuoteReserved}).");
        }

        QuoteTotal += newCapital - StartingCapital;
        StartingCapital = newCapital;
        EnsureInvariants();
    }

    internal void ReserveQuote(decimal amount)
    {
        QuoteReserved += amount;
        EnsureInvariants();
    }

    internal void ReleaseQuote(decimal amount)
    {
        QuoteReserved -= amount;
        EnsureInvariants();
    }

    internal void ReserveBase(decimal amount)
    {
        BaseReserved += amount;
        EnsureInvariants();
    }

    internal void ReleaseBase(decimal amount)
    {
        BaseReserved -= amount;
        EnsureInvariants();
    }

    internal void BuyWithQuoteFee(decimal quoteSpent, decimal grossBase, decimal fee, decimal price)
    {
        QuoteTotal -= quoteSpent + fee;
        BaseTotal += grossBase;
        CostBasis += quoteSpent + fee;
        FeesInCost += fee;
        FeesQuote += fee;
        LastBuyPrice = price;
        EnsureInvariants();
    }

    /// <summary>
    ///     Списывает потраченную котируемую валюту и зачисляет базовый актив за вычетом комиссии; стоимость позиции растёт
    ///     на всю потраченную сумму.
    /// </summary>
    /// <summary>
    ///     Opens or adds to a margin run's long: the whole notional and its fee leave the cash view, the position's margin stays
    ///     reserved through <see cref="MarginRelease" />.
    /// </summary>
    internal void OpenLong(decimal quoteSpent, decimal grossBase, decimal fee, decimal price)
    {
        EntryNotional += quoteSpent;
        BuyWithQuoteFee(quoteSpent, grossBase, fee, price);
    }

    /// <summary>
    ///     Opens or adds to a short: the base balance goes below zero by the quantity sold, the proceeds less the fee are credited and
    ///     kept as the short's cost basis, and the price is the last entry price.
    /// </summary>
    internal void OpenShort(decimal grossBase, decimal grossQuote, decimal fee, decimal price)
    {
        QuoteTotal += grossQuote - fee;
        BaseTotal -= grossBase;
        CostBasis += grossQuote - fee;
        FeesInCost += fee;
        EntryNotional += grossQuote;
        FeesQuote += fee;
        LastBuyPrice = price;
        EnsureInvariants();
    }

    /// <summary>
    ///     Buys back part or all of a short. The cover is charged the share of the short's net proceeds of the quantity it covers;
    ///     realized profit = that share − the cover's cost − its fee.
    /// </summary>
    internal decimal Cover(decimal grossBase, decimal grossQuote, decimal fee)
    {
        decimal owed = -BaseTotal;
        decimal allocated = grossBase == owed ? CostBasis : CostBasis * grossBase / owed;
        FeesInCost -= grossBase == owed ? FeesInCost : FeesInCost * grossBase / owed;
        decimal realized = allocated - grossQuote - fee;
        EntryNotional -= grossBase == owed ? EntryNotional : EntryNotional * grossBase / owed;
        BaseTotal += grossBase;
        QuoteTotal -= grossQuote + fee;
        CostBasis -= allocated;
        RealizedPnl += realized;
        FeesQuote += fee;
        EnsureInvariants();

        return realized;
    }

    internal void Buy(decimal quoteSpent, decimal grossBase, decimal fee, decimal price)
    {
        QuoteTotal -= quoteSpent;
        BaseTotal += grossBase - fee;
        CostBasis += quoteSpent;
        FeesInCost += fee == grossBase ? quoteSpent : quoteSpent * fee / grossBase;
        FeesBase += fee;
        LastBuyPrice = price;
        EnsureInvariants();
    }

    /// <summary>
    ///     Списывает проданное количество и зачисляет выручку за вычетом комиссии. Продаже приписывается доля стоимости
    ///     позиции, пропорциональная проданному количеству; реализованный результат = чистая выручка − эта доля.
    /// </summary>
    internal decimal Sell(decimal grossBase, decimal grossQuote, decimal fee)
    {
        decimal allocated = grossBase == BaseTotal ? CostBasis : CostBasis * grossBase / BaseTotal;
        FeesInCost -= grossBase == BaseTotal ? FeesInCost : FeesInCost * grossBase / BaseTotal;
        decimal realized = grossQuote - fee - allocated;

        if (_leverage is not null)
        {
            EntryNotional -= grossBase == BaseTotal ? EntryNotional : EntryNotional * grossBase / BaseTotal;
        }

        BaseTotal -= grossBase;
        QuoteTotal += grossQuote - fee;
        CostBasis -= allocated;
        RealizedPnl += realized;
        FeesQuote += fee;
        EnsureInvariants();

        return realized;
    }

    internal void CorrectQuoteFee(decimal fee, decimal retainedCost)
    {
        if (fee <= 0m || retainedCost < 0m || retainedCost > fee)
        {
            throw new ArgumentOutOfRangeException(nameof(fee));
        }

        QuoteTotal -= fee;
        FeesQuote += fee;
        CostBasis += retainedCost;
        FeesInCost += retainedCost;
        RealizedPnl -= fee - retainedCost;
        EnsureInvariants();
    }

    internal void ConfigureExternalQuoteFees()
    {
        _externalQuoteShortfallAllowed = true;
    }

    internal void SetPendingFeeReserve(decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        PendingFeeReserve = amount;
    }

    private void EnsureInvariants()
    {
        bool wrongSide = _direction == PositionDirection.Long ? BaseTotal < 0m : BaseTotal > 0m;

        if (QuoteReserved < 0m || BaseReserved < 0m || (!_externalQuoteShortfallAllowed && QuoteReserved > QuoteTotal) || BaseReserved > Math.Abs(BaseTotal) ||
            CostBasis < 0m || FeesInCost < 0m || wrongSide || EntryNotional < 0m || (_leverage is null && EntryNotional != 0m))
        {
            throw new TradingInvariantException(
                $"Portfolio invariant violated: quote {QuoteTotal} (reserved {QuoteReserved}), base {BaseTotal} (reserved {BaseReserved}), cost basis {CostBasis}.");
        }
    }
}