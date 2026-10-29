using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Accounting;

/// <summary>
///     Margin runs (ADR 0061): a short sells first and holds a negative base balance, an entry reserves its notional divided by the
///     leverage plus its fee, and every fee is a quote fee. Fees of 0.1 % throughout.
/// </summary>
public sealed class MarginAccountTests
{
    private static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 0.01m,
        priceTick: 0.01m,
        minQuantity: 0.01m,
        minNotional: 1m,
        basePrecision: 2,
        quotePrecision: 2,
        FeeAsset.Quote,
        exactQuoteFees: true);

    [Test]
    public void AShortOpensInPartsAndItsCoverRealizesTheDifference()
    {
        var account = new ExternallyExecutedAccount(Settings(capital: 10000m).WithDirection(PositionDirection.Short), Rules);
        Order entry = account.Submit(Sell(quantity: 10m, price: 100m, "entry")).Order.ShouldNotBeNull();

        account.Capture().QuoteReserved.ShouldBe(1001m);
        account.Capture().QuoteAvailable.ShouldBe(8999m);

        account.ApplyExternalUpdate(ExternalOrderUpdate.Accept(entry.Id), rejectionAttemptId: 1);
        account.ApplyExternalUpdate(ExternalOrderUpdate.Partial(entry.Id, grossBase: 4m, grossQuote: 400m, fee: 0.4m), rejectionAttemptId: 1);

        AccountSnapshot partial = account.Capture();
        partial.BaseTotal.ShouldBe(-4m);
        partial.QuoteTotal.ShouldBe(10399.6m);
        partial.QuoteReserved.ShouldBe(600.6m);
        partial.QuoteAvailable.ShouldBe(8999m);

        account.ApplyExternalUpdate(ExternalOrderUpdate.Final(entry.Id, grossBase: 10m, grossQuote: 1000m, fee: 1m), rejectionAttemptId: 1);

        AccountSnapshot sold = account.Capture();
        sold.BaseTotal.ShouldBe(-10m);
        sold.QuoteTotal.ShouldBe(10999m);
        sold.QuoteReserved.ShouldBe(0m);
        sold.CostBasis.ShouldBe(999m);
        sold.QuoteAvailable.ShouldBe(8999m);
        sold.BaseAvailable.ShouldBe(10m);

        Order cover = account.Submit(Buy(quantity: 10m, price: 90m, "target")).Order.ShouldNotBeNull();
        account.Capture().BaseReserved.ShouldBe(10m);
        account.Capture().BaseAvailable.ShouldBe(0m);
        account.Submit(Buy(quantity: 1m, price: 90m, "more")).Rejection.ShouldNotBeNull().Reason.ShouldBe(RejectionReason.InsufficientBase);

        ExternallyExecutedAccount restored = ExternallyExecutedAccount.Restore(
            account.CaptureCheckpoint(),
            Settings(capital: 10000m).WithDirection(PositionDirection.Short),
            Rules);
        restored.Capture().QuoteAvailable.ShouldBe(8999m);
        restored.Capture().BaseTotal.ShouldBe(-10m);

        restored.ApplyExternalUpdate(ExternalOrderUpdate.Accept(cover.Id), rejectionAttemptId: 1);
        restored.ApplyExternalUpdate(ExternalOrderUpdate.Final(cover.Id, grossBase: 10m, grossQuote: 900m, fee: 0.9m), rejectionAttemptId: 1);

        AccountSnapshot closed = restored.Capture();
        closed.BaseTotal.ShouldBe(0m);
        closed.CostBasis.ShouldBe(0m);
        closed.RealizedPnl.ShouldBe(98.1m);
        closed.QuoteTotal.ShouldBe(10098.1m);
        closed.QuoteAvailable.ShouldBe(10098.1m);
        restored.CaptureCheckpoint().EntryNotional.ShouldBe(0m);
    }

    [Test]
    public void AShortCannotSellMoreThanItsMarginAllows()
    {
        var account = new TradingAccount(Settings(capital: 500m).WithMargin(leverage: 2m).WithDirection(PositionDirection.Short), Rules);

        // 10 at 100 at 2× needs 500 of margin and 1 of fee.
        account.Submit(Sell(quantity: 10m, price: 100m, "entry")).Rejection.ShouldNotBeNull().Reason.ShouldBe(RejectionReason.InsufficientQuote);
        account.Submit(Sell(quantity: 9.98m, price: 100m, "entry")).Order.ShouldNotBeNull().Reservation.ShouldBe(499.998m);
    }

    [Test]
    public void ALeveragedLongBuysWithItsMarginAndSellsForTheWholeDifference()
    {
        var account = new TradingAccount(Settings(capital: 100m).WithMargin(leverage: 3m), Rules);

        account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 300m, LimitPrice: 10m, "big", "buy", EligibleFromCandleIndex: 0))
            .Rejection.ShouldNotBeNull()
            .Reason.ShouldBe(RejectionReason.InsufficientQuote);

        Order buy = account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Limit, Amount: 297m, LimitPrice: 10m, "entry", "buy", EligibleFromCandleIndex: 0))
            .Order.ShouldNotBeNull();
        buy.Reservation.ShouldBe(99.297m);
        account.Execute(buy.Id, referencePrice: 10m);

        AccountSnapshot bought = account.Capture();
        bought.BaseTotal.ShouldBe(29.7m);
        bought.QuoteTotal.ShouldBe(-197.297m);
        bought.QuoteAvailable.ShouldBe(0.703m);

        Order sell = account.Submit(Sell(quantity: 29.7m, price: 11m, "exit")).Order.ShouldNotBeNull();
        account.Execute(sell.Id, referencePrice: 11m).Fill.ShouldNotBeNull().RealizedPnl.ShouldBe(29.0763m);

        AccountSnapshot sold = account.Capture();
        sold.BaseTotal.ShouldBe(0m);
        sold.QuoteTotal.ShouldBe(129.0763m);
        sold.QuoteAvailable.ShouldBe(129.0763m);
    }

    [Test]
    public void AShortMarketEntryIsValuedAtTheSlippedReferencePrice()
    {
        var account = new TradingAccount(Settings(capital: 1000m, slippage: 0.01m).WithDirection(PositionDirection.Short), Rules);
        var request = new OrderRequest(OrderSide.Sell, OrderType.Market, Amount: 5m, LimitPrice: null, "entry", "sell", EligibleFromCandleIndex: 0, ReferencePrice: 100m);

        Order order = account.Submit(request).Order.ShouldNotBeNull();

        // 5 at 99 and its fee of 0.1 %.
        order.Reservation.ShouldBe(495.495m);
        account.Execute(order.Id, referencePrice: 100m).Fill.ShouldNotBeNull().Price.ShouldBe(99m);
        account.Capture().BaseTotal.ShouldBe(-5m);
        account.Submit(new OrderRequest(OrderSide.Sell, OrderType.Market, Amount: 5m, LimitPrice: null, "again", "sell", EligibleFromCandleIndex: 0))
            .Rejection.ShouldNotBeNull()
            .Reason.ShouldBe(RejectionReason.InvalidPrice);
    }

    [Test]
    public void AMarginRunNeedsQuoteFeesAndNeitherSellerFeesNorAHold()
    {
        Should.Throw<TradingInvariantException>(() => new TradingAccount(
            Settings(capital: 100m).WithDirection(PositionDirection.Short),
            new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0m, minNotional: 0m, basePrecision: 2, quotePrecision: 2)));
        Should.Throw<TradingInvariantException>(() => Settings(capital: 100m).WithMargin(leverage: 0.5m));
        Should.Throw<TradingInvariantException>(() => Settings(capital: 100m).WithSettlementHold(TimeSpan.FromDays(1)).WithMargin(leverage: 1m));
        Settings(capital: 100m).WithDirection(PositionDirection.Short).Leverage.ShouldBe(1m);
        Settings(capital: 100m).Direction.ShouldBe(PositionDirection.Long);
        Settings(capital: 100m).IsMargin.ShouldBeFalse();
    }

    private static ExecutionSettings Settings(decimal capital, decimal slippage = 0m)
    {
        return new ExecutionSettings(capital, slippage, limitFeeRate: 0.001m, marketFeeRate: 0.001m);
    }

    private static OrderRequest Sell(decimal quantity, decimal price, string tag)
    {
        return new OrderRequest(OrderSide.Sell, OrderType.Limit, quantity, price, tag, "sell", EligibleFromCandleIndex: 0);
    }

    private static OrderRequest Buy(decimal quantity, decimal price, string tag)
    {
        return new OrderRequest(OrderSide.Buy, OrderType.Limit, quantity, price, tag, "cover", EligibleFromCandleIndex: 0);
    }
}
