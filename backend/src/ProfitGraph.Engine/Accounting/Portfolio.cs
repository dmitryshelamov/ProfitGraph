namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Балансы одного прогона: итоговые и зарезервированные суммы обоих активов, средневзвешенная стоимость позиции,
///     реализованный результат и удержанные комиссии. Equity включает резервы, поэтому выставление ордера не меняет PnL.
///     Стоимость позиции не округляется к шагу количества; пыль остаётся в позиции и в equity.
/// </summary>
public sealed class Portfolio
{
    private bool _externalQuoteShortfallAllowed;
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

    public decimal QuoteAvailable => QuoteTotal - QuoteReserved - PendingFeeReserve;
    public decimal BaseAvailable => BaseTotal - BaseReserved;

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
        EnsureInvariants();
    }

    public decimal Equity(decimal close)
    {
        return QuoteTotal + BaseTotal * close;
    }

    public decimal UnrealizedPnl(decimal close)
    {
        return BaseTotal * close - CostBasis;
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
        FeesQuote += fee;
        LastBuyPrice = price;
        EnsureInvariants();
    }

    /// <summary>
    ///     Списывает потраченную котируемую валюту и зачисляет базовый актив за вычетом комиссии; стоимость позиции растёт
    ///     на всю потраченную сумму.
    /// </summary>
    internal void Buy(decimal quoteSpent, decimal grossBase, decimal fee, decimal price)
    {
        QuoteTotal -= quoteSpent;
        BaseTotal += grossBase - fee;
        CostBasis += quoteSpent;
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
        decimal realized = grossQuote - fee - allocated;
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
        if (QuoteReserved < 0m || BaseReserved < 0m || (!_externalQuoteShortfallAllowed && QuoteReserved > QuoteTotal) || BaseReserved > BaseTotal || CostBasis < 0m)
        {
            throw new TradingInvariantException(
                $"Portfolio invariant violated: quote {QuoteTotal} (reserved {QuoteReserved}), base {BaseTotal} (reserved {BaseReserved}), cost basis {CostBasis}.");
        }
    }
}