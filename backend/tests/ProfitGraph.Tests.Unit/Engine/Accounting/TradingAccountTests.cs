using System.Globalization;

using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Engine.Accounting;

public sealed class TradingAccountTests
{
    private static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 0.000001m,
        priceTick: 0.01m,
        minQuantity: 0.000001m,
        minNotional: 1m,
        basePrecision: 8,
        quotePrecision: 8);

    private static TradingAccount CreateAccount(decimal capital = 1000m, decimal slippage = 0m, decimal limitFee = 0.001m, decimal marketFee = 0.001m)
    {
        return new TradingAccount(new ExecutionSettings(capital, slippage, limitFee, marketFee), Rules);
    }

    private static OrderRequest Request(OrderSide side, OrderType type, decimal amount, decimal? price = null, string? tag = null)
    {
        return new OrderRequest(side, type, amount, price, tag, "node", EligibleFromCandleIndex: 0);
    }

    private static Order Submit(TradingAccount account, OrderRequest request)
    {
        OrderSubmission submission = account.Submit(request);
        submission.Rejection.ShouldBeNull();

        return submission.Order!;
    }

    private static Fill Execute(TradingAccount account, Order order, decimal referencePrice = 0m)
    {
        OrderExecution execution = account.Execute(order.Id, referencePrice);
        execution.Rejection.ShouldBeNull();

        return execution.Fill!;
    }

    private static void ShouldReconcile(TradingAccount account, decimal close)
    {
        Portfolio portfolio = account.Portfolio;
        (portfolio.Equity(close) - portfolio.StartingCapital).ShouldBe(portfolio.RealizedPnl + portfolio.UnrealizedPnl(close));
        portfolio.QuoteReserved.ShouldBe(account.Orders.Active.Where(order => order.Side == OrderSide.Buy).Sum(order => order.Reservation));
        portfolio.BaseReserved.ShouldBe(account.Orders.Active.Where(order => order.Side == OrderSide.Sell).Sum(order => order.Reservation));
    }

    [Test]
    public void RequirementsFixture_BuyAndSellWithFeesInTheirAssets()
    {
        TradingAccount account = CreateAccount(capital: 100m);

        Fill buy = Execute(account, Submit(account, Request(OrderSide.Buy, OrderType.Market, amount: 100m)), referencePrice: 100_000m);

        buy.GrossBase.ShouldBe(0.001m);
        buy.Fee.ShouldBe(0.000001m);
        buy.FeeAsset.ShouldBe(FeeAsset.Base);
        buy.BaseChange.ShouldBe(0.000999m);
        buy.QuoteChange.ShouldBe(-100m);
        account.Portfolio.QuoteTotal.ShouldBe(0m);
        account.Portfolio.BaseTotal.ShouldBe(0.000999m);
        account.Portfolio.CostBasis.ShouldBe(100m);

        Fill sell = Execute(account, Submit(account, Request(OrderSide.Sell, OrderType.Market, amount: 0.000999m)), referencePrice: 100_000m);

        sell.Fee.ShouldBe(0.0999m);
        sell.FeeAsset.ShouldBe(FeeAsset.Quote);
        sell.QuoteChange.ShouldBe(99.8001m);
        sell.RealizedPnl.ShouldBe(-0.1999m);
        account.Portfolio.QuoteTotal.ShouldBe(99.8001m);
        account.Portfolio.BaseTotal.ShouldBe(0m);
        account.Portfolio.CostBasis.ShouldBe(0m);
        account.Portfolio.FeesBase.ShouldBe(0.000001m);
        account.Portfolio.FeesQuote.ShouldBe(0.0999m);
        ShouldReconcile(account, close: 100_000m);
    }

    [Test]
    public void MarketBuy_UsesSlippedPrice_AndCapturedBudgetAfterGap()
    {
        TradingAccount account = CreateAccount(slippage: 0.001m, marketFee: 0m);
        Order order = Submit(account, Request(OrderSide.Buy, OrderType.Market, amount: 100m));

        Fill fill = Execute(account, order, referencePrice: 150m);

        fill.Price.ShouldBe(150.15m);
        fill.GrossBase.ShouldBe(0.666000m);
        fill.QuoteChange.ShouldBe(-0.666m * 150.15m);
        account.Portfolio.QuoteReserved.ShouldBe(0m);
        account.Portfolio.QuoteTotal.ShouldBe(1000m - 0.666m * 150.15m);
        ShouldReconcile(account, close: 151m);
    }

    [Test]
    public void LimitBuy_ReservesNormalizedCost_WithoutChangingEquity_AndFillsAtLimit()
    {
        TradingAccount account = CreateAccount(limitFee: 0m);
        Order order = Submit(account, Request(OrderSide.Buy, OrderType.Limit, amount: 100m, price: 99.999m));

        order.LimitPrice.ShouldBe(99.99m);
        order.Quantity.ShouldBe(1.000100m);
        order.Reservation.ShouldBe(1.0001m * 99.99m);
        account.Portfolio.QuoteAvailable.ShouldBe(1000m - 1.0001m * 99.99m);
        account.Portfolio.Equity(close: 105m).ShouldBe(1000m);

        Fill fill = Execute(account, order, referencePrice: 50m);

        fill.Price.ShouldBe(99.99m);
        fill.QuoteChange.ShouldBe(-1.0001m * 99.99m);
        order.Status.ShouldBe(OrderStatus.Filled);
        account.Portfolio.QuoteReserved.ShouldBe(0m);
        ShouldReconcile(account, close: 105m);
    }

    [Test]
    public void SecondBuyForAllAvailableQuote_IsRejected_WithoutNegativeBalance()
    {
        TradingAccount account = CreateAccount();
        Submit(account, Request(OrderSide.Buy, OrderType.Market, amount: 1000m));

        OrderSubmission second = account.Submit(Request(OrderSide.Buy, OrderType.Limit, amount: 1000m, price: 10m));

        second.IsAccepted.ShouldBeFalse();
        second.Rejection!.Reason.ShouldBe(RejectionReason.InsufficientQuote);
        second.Rejection.OriginNodeId.ShouldBe("node");
        account.Portfolio.QuoteAvailable.ShouldBe(0m);
        account.Orders.Active.Count.ShouldBe(1);
    }

    [Test]
    public void MarketBuyWithQuoteFee_NeedsTheBudgetAndItsFee_WithoutNegativeBalance()
    {
        // GAZP, September 2023: lots rounded at the next open spent 90 060.5736 of 90 052.72.
        var rules = new InstrumentRules(
            quantityStep: 10m,
            priceTick: 0.01m,
            minQuantity: 10m,
            minNotional: 0m,
            basePrecision: 0,
            quotePrecision: 2,
            FeeAsset.Quote,
            exactQuoteFees: true);
        var settings = new ExecutionSettings(startingCapital: 90052.72m, slippage: 0m, limitFeeRate: 0.003m, marketFeeRate: 0.003m);
        var underfunded = new TradingAccount(settings, rules);

        OrderSubmission rejected = underfunded.Submit(Request(OrderSide.Buy, OrderType.Market, amount: 90000m));

        rejected.IsAccepted.ShouldBeFalse();
        rejected.Rejection!.Reason.ShouldBe(RejectionReason.InsufficientQuote);
        underfunded.Portfolio.QuoteAvailable.ShouldBe(90052.72m);

        var funded = new TradingAccount(new ExecutionSettings(startingCapital: 90270m, slippage: 0m, limitFeeRate: 0.003m, marketFeeRate: 0.003m), rules);
        Order order = Submit(funded, Request(OrderSide.Buy, OrderType.Market, amount: 90000m));
        Fill fill = Execute(funded, order, referencePrice: 166.28m);

        fill.BaseChange.ShouldBe(540m);
        fill.QuoteChange.ShouldBe(-90060.5736m);
        funded.Portfolio.QuoteAvailable.ShouldBe(209.4264m);
    }

    [Test]
    public void SellAndBuyReservations_DoNotConflict()
    {
        TradingAccount account = CreateAccount(marketFee: 0m);
        Execute(account, Submit(account, Request(OrderSide.Buy, OrderType.Market, amount: 500m)), referencePrice: 100m);

        Order exit = Submit(account, Request(OrderSide.Sell, OrderType.Limit, amount: 5m, price: 110m, "exit"));
        Order average = Submit(account, Request(OrderSide.Buy, OrderType.Limit, amount: 500m, price: 90m, "average"));

        account.Portfolio.BaseAvailable.ShouldBe(0m);
        account.Portfolio.QuoteAvailable.ShouldBe(500m - 5.555555m * 90m);
        exit.IsActive.ShouldBeTrue();
        average.IsActive.ShouldBeTrue();
        ShouldReconcile(account, close: 100m);
    }

    [Test]
    public void ActiveTag_RejectsSecondOrder_AndTerminalOrderReleasesTag()
    {
        TradingAccount account = CreateAccount();
        Order first = Submit(account, Request(OrderSide.Buy, OrderType.Limit, amount: 100m, price: 50m, "entry"));

        account.Submit(Request(OrderSide.Buy, OrderType.Limit, amount: 100m, price: 40m, "entry")).Rejection!.Reason.ShouldBe(RejectionReason.TagActive);
        account.CancelByTag("entry").ShouldBe(first);
        Order second = Submit(account, Request(OrderSide.Buy, OrderType.Limit, amount: 100m, price: 40m, "entry"));

        first.Status.ShouldBe(OrderStatus.Cancelled);
        second.Id.ShouldBeGreaterThan(first.Id);
        account.Orders.GetActive("entry").ShouldBe(second);
        account.Portfolio.QuoteReserved.ShouldBe(second.Reservation);
    }

    [Test]
    public void CancellingAbsentOrTerminalOrder_IsNoOp()
    {
        TradingAccount account = CreateAccount();
        Order order = Submit(account, Request(OrderSide.Buy, OrderType.Limit, amount: 100m, price: 50m, "entry"));
        account.Cancel(order.Id).ShouldBe(order);

        account.Cancel(order.Id).ShouldBeNull();
        account.Cancel(orderId: 999).ShouldBeNull();
        account.CancelByTag("entry").ShouldBeNull();
        account.Portfolio.QuoteReserved.ShouldBe(0m);
        Should.Throw<TradingInvariantException>(() => account.Execute(order.Id, referencePrice: 50m));
    }

    [TestCase(OrderSide.Buy, OrderType.Limit, "0", "10", RejectionReason.InvalidAmount)]
    [TestCase(OrderSide.Buy, OrderType.Limit, "100", "0.001", RejectionReason.InvalidPrice)]
    [TestCase(OrderSide.Buy, OrderType.Market, "0.5", null, RejectionReason.BelowMinNotional)]
    [TestCase(OrderSide.Buy, OrderType.Limit, "0.5", "10", RejectionReason.BelowMinNotional)]
    [TestCase(OrderSide.Buy, OrderType.Limit, "0.000001", "10", RejectionReason.ZeroQuantity)]
    [TestCase(OrderSide.Sell, OrderType.Market, "0.0000009", null, RejectionReason.ZeroQuantity)]
    [TestCase(OrderSide.Sell, OrderType.Market, "1", null, RejectionReason.InsufficientBase)]
    public void Submission_RejectsWithoutChangingBalances(OrderSide side, OrderType type, string amount, string? price, RejectionReason reason)
    {
        TradingAccount account = CreateAccount();

        OrderSubmission submission = account.Submit(
            Request(side, type, decimal.Parse(amount, CultureInfo.InvariantCulture), price is null ? null : decimal.Parse(price, CultureInfo.InvariantCulture)));

        submission.Rejection!.Reason.ShouldBe(reason);
        submission.Rejection.OrderId.ShouldBeNull();
        account.Portfolio.QuoteReserved.ShouldBe(0m);
        account.Portfolio.QuoteTotal.ShouldBe(1000m);
        account.Orders.Active.ShouldBeEmpty();
    }

    [Test]
    public void MarketOrderBelowMinimumAtExecution_IsRejected_AndReleasesReservation()
    {
        TradingAccount account = CreateAccount();
        Order order = Submit(account, Request(OrderSide.Buy, OrderType.Market, amount: 1m, tag: "entry"));

        OrderExecution execution = account.Execute(order.Id, referencePrice: 2_000_000m);

        execution.IsFilled.ShouldBeFalse();
        execution.Rejection!.Reason.ShouldBe(RejectionReason.ZeroQuantity);
        execution.Rejection.OrderId.ShouldBe(order.Id);
        order.Status.ShouldBe(OrderStatus.Rejected);
        account.Portfolio.QuoteReserved.ShouldBe(0m);
        account.Orders.GetActive("entry").ShouldBeNull();
    }

    [Test]
    public void WeightedAverageCost_PartialSellLeavesDust_AndReconciles()
    {
        TradingAccount account = CreateAccount(limitFee: 0.001m, marketFee: 0.001m);
        Execute(account, Submit(account, Request(OrderSide.Buy, OrderType.Market, amount: 300m)), referencePrice: 100m);
        Execute(account, Submit(account, Request(OrderSide.Buy, OrderType.Limit, amount: 300m, price: 80m)));
        decimal position = account.Portfolio.BaseTotal;
        decimal costBasis = account.Portfolio.CostBasis;

        Fill sell = Execute(account, Submit(account, Request(OrderSide.Sell, OrderType.Limit, position - 0.0000005m, price: 95m)));

        decimal sold = sell.GrossBase;
        sold.ShouldBe(position - 0.000001m);
        sell.RealizedPnl.ShouldBe(sold * 95m - sell.Fee - costBasis * sold / position);
        account.Portfolio.BaseTotal.ShouldBe(0.000001m);
        account.Portfolio.CostBasis.ShouldBe(costBasis - costBasis * sold / position);
        account.Portfolio.Equity(close: 95m).ShouldBe(account.Portfolio.QuoteTotal + 0.000001m * 95m);
        ShouldReconcile(account, close: 95m);
    }

    [Test]
    public void ReleaseAll_CancelsEveryActiveOrder_WithoutSelling()
    {
        TradingAccount account = CreateAccount(marketFee: 0m);
        Execute(account, Submit(account, Request(OrderSide.Buy, OrderType.Market, amount: 500m)), referencePrice: 100m);
        Submit(account, Request(OrderSide.Sell, OrderType.Limit, amount: 5m, price: 110m));
        Submit(account, Request(OrderSide.Buy, OrderType.Market, amount: 100m));

        IReadOnlyList<Order> released = account.ReleaseAll();

        released.Select(order => order.Status).ShouldBe(new[] { OrderStatus.Cancelled, OrderStatus.Cancelled });
        account.Orders.Active.ShouldBeEmpty();
        account.Portfolio.BaseTotal.ShouldBe(5m);
        account.Portfolio.QuoteReserved.ShouldBe(0m);
        account.Portfolio.BaseReserved.ShouldBe(0m);
    }

    [Test]
    public void Eligibility_StartsFromRequestedCandle()
    {
        TradingAccount account = CreateAccount();
        Order order = Submit(account, Request(OrderSide.Buy, OrderType.Market, amount: 100m) with { EligibleFromCandleIndex = 3 });

        order.IsEligibleAt(candleIndex: 2).ShouldBeFalse();
        order.IsEligibleAt(candleIndex: 3).ShouldBeTrue();
    }

    [Test]
    public void InvalidSettingsAndRequests_AreInvariantViolations()
    {
        Should.Throw<TradingInvariantException>(() => new ExecutionSettings(startingCapital: 0m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m));
        Should.Throw<TradingInvariantException>(() => new ExecutionSettings(startingCapital: 1m, slippage: 1m, limitFeeRate: 0m, marketFeeRate: 0m));
        Should.Throw<TradingInvariantException>(() => new InstrumentRules(
            quantityStep: 0m,
            priceTick: 1m,
            minQuantity: 0m,
            minNotional: 0m,
            basePrecision: 8,
            quotePrecision: 8));
        TradingAccount account = CreateAccount();
        Should.Throw<TradingInvariantException>(() => account.Submit(Request(OrderSide.Buy, OrderType.Limit, amount: 10m)));
        Should.Throw<TradingInvariantException>(() => account.Submit(Request(OrderSide.Buy, OrderType.Market, amount: 10m, price: 10m)));
    }
}