using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class OrderMatcherTests
{
    [TestCase(OrderSide.Buy, true, 110, 100)]
    [TestCase(OrderSide.Buy, false, 110, 100)]
    [TestCase(OrderSide.Buy, true, 90, 0)]
    [TestCase(OrderSide.Buy, false, 90, 90)]
    [TestCase(OrderSide.Sell, true, 90, 100)]
    [TestCase(OrderSide.Sell, false, 90, 100)]
    [TestCase(OrderSide.Sell, true, 110, 110)]
    [TestCase(OrderSide.Sell, false, 110, 0)]
    [TestCase(OrderSide.Buy, true, 100, 100)]
    [TestCase(OrderSide.Sell, false, 100, 100)]
    [TestCase(OrderSide.Buy, false, 80, 80)]
    [TestCase(OrderSide.Sell, true, 120, 120)]
    [TestCase(OrderSide.Buy, false, 70, 0)]
    [TestCase(OrderSide.Sell, true, 130, 0)]
    public void SideDirectionAndCurrentPoint_SelectOnlyExecutableRemainingLevels(OrderSide side, bool ascending, int limit, int expectedPrice)
    {
        TradingAccount account = Account();
        Order order = Submit(account, side, limit);
        PriceSegment segment = Segment(ascending);

        OrderMatchCandidate? candidate = OrderMatcher.Next(segment, currentPrice: 100m, account, candleIndex: 0);

        if (expectedPrice == 0)
        {
            candidate.ShouldBeNull();
        }
        else
        {
            candidate.ShouldBe(new OrderMatchCandidate(order.Id, expectedPrice));
        }

        order.IsActive.ShouldBeTrue();
        account.Orders.Active.ShouldContain(order);
    }

    [TestCase(OrderSide.Buy, arg2: 90, arg3: false)]
    [TestCase(OrderSide.Buy, arg2: 100, arg3: true)]
    [TestCase(OrderSide.Buy, arg2: 110, arg3: true)]
    [TestCase(OrderSide.Sell, arg2: 90, arg3: true)]
    [TestCase(OrderSide.Sell, arg2: 100, arg3: true)]
    [TestCase(OrderSide.Sell, arg2: 110, arg3: false)]
    public void OpenPoint_UsesInclusiveSideThresholds(OrderSide side, int limit, bool executable)
    {
        TradingAccount account = Account();
        Order order = Submit(account, side, limit);

        OrderMatchCandidate? candidate = OrderMatcher.NextAtPrice(price: 100m, account, candleIndex: 0);

        candidate.ShouldBe(executable ? new OrderMatchCandidate(order.Id, pathPrice: 100m) : null);
    }

    [Test]
    public void FlatCandle_MatchesAtPreservedBoundaryWithoutInventingSegments()
    {
        TradingAccount account = Account();
        Order buy = Submit(account, OrderSide.Buy, limit: 100m);
        var path = PricePath.Build(CandleGenerator.Generate() with { Open = 100m, Low = 100m, High = 100m, Close = 100m });

        path.Segments.ShouldBeEmpty();
        OrderMatcher.NextAtPrice(path.Open, account, candleIndex: 0).ShouldBe(new OrderMatchCandidate(buy.Id, path.Open));
        account.Execute(buy.Id, path.Open);
        OrderMatcher.NextAtPrice(path.Close, account, candleIndex: 0).ShouldBeNull();
    }

    [Test]
    public void SavedNormalizedDecimalLimit_IsTheExecutionPriceAndReservationReconciles()
    {
        TradingAccount account = Account();
        Order order = Submit(account, OrderSide.Sell, limit: 105.019m);
        decimal baseBefore = account.Portfolio.BaseTotal;
        decimal quoteBefore = account.Portfolio.QuoteTotal;
        OrderMatchCandidate candidate = OrderMatcher.Next(Segment(ascending: true), currentPrice: 100m, account, candleIndex: 0)!;

        candidate.PathPrice.ShouldBe(105.01m);
        candidate.OrderId.ShouldBe(order.Id);
        OrderExecution execution = account.Execute(candidate.OrderId, candidate.PathPrice);

        execution.Fill!.Price.ShouldBe(105.01m);
        order.Status.ShouldBe(OrderStatus.Filled);
        account.Portfolio.BaseTotal.ShouldBe(baseBefore - 1m);
        account.Portfolio.QuoteTotal.ShouldBe(quoteBefore + 105.01m);
        account.Portfolio.BaseReserved.ShouldBe(0m);
        account.Portfolio.BaseAvailable.ShouldBe(account.Portfolio.BaseTotal);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NearestLevelWinsBeforeCreationId_ThenEqualLevelsUseId(bool ascending)
    {
        TradingAccount account = Account();
        OrderSide side = ascending ? OrderSide.Sell : OrderSide.Buy;
        Order far = Submit(account, side, ascending ? 115m : 85m);
        Order near = Submit(account, side, ascending ? 105m : 95m);
        Order tied = Submit(account, side, near.LimitPrice!.Value);
        PriceSegment segment = Segment(ascending);

        OrderMatchCandidate? first = OrderMatcher.Next(segment, currentPrice: 100m, account, candleIndex: 0);

        first.ShouldBe(new OrderMatchCandidate(near.Id, near.LimitPrice!.Value));
        account.Execute(first!.OrderId, first.PathPrice).IsFilled.ShouldBeTrue();
        OrderMatchCandidate? second = OrderMatcher.Next(segment, first.PathPrice, account, candleIndex: 0);
        second.ShouldBe(new OrderMatchCandidate(tied.Id, first.PathPrice));
        account.Execute(second!.OrderId, second.PathPrice).IsFilled.ShouldBeTrue();
        OrderMatcher.Next(segment, second.PathPrice, account, candleIndex: 0)
            .ShouldBe(new OrderMatchCandidate(far.Id, far.LimitPrice!.Value));
    }

    [Test]
    public void GapTies_UseCreationIdAcrossSidesAndPreservePathPrice()
    {
        TradingAccount account = Account();
        Order sell = Submit(account, OrderSide.Sell, limit: 90m);
        Order buy = Submit(account, OrderSide.Buy, limit: 110m);
        decimal baseBefore = account.Portfolio.BaseTotal;
        decimal quoteBefore = account.Portfolio.QuoteTotal;

        OrderMatchCandidate? candidate = OrderMatcher.NextAtPrice(price: 100m, account, candleIndex: 0);

        candidate.ShouldBe(new OrderMatchCandidate(sell.Id, pathPrice: 100m));
        account.Portfolio.BaseTotal.ShouldBe(baseBefore);
        account.Portfolio.QuoteTotal.ShouldBe(quoteBefore);
        OrderExecution execution = account.Execute(candidate!.OrderId, candidate.PathPrice);
        execution.Fill.ShouldNotBeNull();
        execution.Fill!.Price.ShouldBe(90m);
        candidate.PathPrice.ShouldBe(100m);
        OrderMatcher.NextAtPrice(price: 100m, account, candleIndex: 0).ShouldBe(new OrderMatchCandidate(buy.Id, pathPrice: 100m));
        account.Execute(buy.Id, referencePrice: 100m).Fill!.Price.ShouldBe(110m);
        account.Portfolio.BaseReserved.ShouldBe(0m);
        account.Portfolio.QuoteReserved.ShouldBe(0m);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SamePointCandidates_PrecedeFutureLevelsEvenWhenCreatedLater(bool ascending)
    {
        TradingAccount account = Account();
        Submit(account, ascending ? OrderSide.Sell : OrderSide.Buy, ascending ? 110m : 90m);
        Order immediate = Submit(account, ascending ? OrderSide.Buy : OrderSide.Sell, ascending ? 110m : 90m);

        OrderMatcher.Next(Segment(ascending), currentPrice: 100m, account, candleIndex: 0)
            .ShouldBe(new OrderMatchCandidate(immediate.Id, pathPrice: 100m));
    }

    [Test]
    public void Requery_SeesCancellationReplacementAndRemainingMovementWithoutRewinding()
    {
        TradingAccount account = Account();
        var segment = new PriceSegment(start: 120m, end: 80m, PricePhase.OpenToLow);
        Order first = Submit(account, OrderSide.Buy, limit: 110m);
        Order cancelled = Submit(account, OrderSide.Buy, limit: 100m);
        OrderMatchCandidate candidate = OrderMatcher.Next(segment, currentPrice: 120m, account, candleIndex: 0)!;
        candidate.OrderId.ShouldBe(first.Id);
        account.Execute(candidate.OrderId, candidate.PathPrice);
        account.Cancel(cancelled.Id);
        Order immediate = Submit(account, OrderSide.Buy, limit: 115m);
        Order future = Submit(account, OrderSide.Buy, limit: 90m);

        OrderMatchCandidate next = OrderMatcher.Next(segment, candidate.PathPrice, account, candleIndex: 0)!;

        next.ShouldBe(new OrderMatchCandidate(immediate.Id, pathPrice: 110m));
        account.Execute(next.OrderId, next.PathPrice).Fill!.Price.ShouldBe(115m);
        OrderMatchCandidate remaining = OrderMatcher.Next(segment, next.PathPrice, account, candleIndex: 0)!;
        remaining.ShouldBe(new OrderMatchCandidate(future.Id, pathPrice: 90m));
        remaining.PathPrice.ShouldBeLessThan(next.PathPrice);
        cancelled.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Test]
    public void CloseCreatedLimit_RemainsIneligibleDuringContinuationAndBecomesEligibleNextCandle()
    {
        TradingAccount account = Account();
        Order order = Submit(account, OrderSide.Buy, limit: 110m, eligibleFrom: 1);
        var segment = new PriceSegment(start: 120m, end: 80m, PricePhase.OpenToLow);

        OrderMatcher.Next(segment, currentPrice: 100m, account, candleIndex: 0).ShouldBeNull();
        OrderMatcher.NextAtPrice(price: 100m, account, candleIndex: 0).ShouldBeNull();
        OrderMatcher.NextAtPrice(price: 100m, account, candleIndex: 1).ShouldBe(new OrderMatchCandidate(order.Id, pathPrice: 100m));
    }

    [Test]
    public void MarketFilledCancelledAndOutOfRangeOrders_AreIgnored()
    {
        TradingAccount account = Account();
        Order filled = Submit(account, OrderSide.Buy, limit: 110m);
        account.Execute(filled.Id, referencePrice: 100m);
        Order cancelled = Submit(account, OrderSide.Sell, limit: 90m);
        account.Cancel(cancelled.Id);
        account.Submit(new OrderRequest(OrderSide.Buy, OrderType.Market, Amount: 100m, LimitPrice: null, Tag: null, "market", EligibleFromCandleIndex: 0))
            .IsAccepted.ShouldBeTrue();
        Submit(account, OrderSide.Sell, limit: 130m);
        Submit(account, OrderSide.Buy, limit: 70m);

        OrderMatcher.Next(Segment(ascending: true), currentPrice: 100m, account, candleIndex: 0).ShouldBeNull();
        OrderMatcher.NextAtPrice(price: 100m, account, candleIndex: 0).ShouldBeNull();
    }

    [Test]
    public void NewlySubmittedUnreachableLimit_CannotReplayAlreadyTraversedMovement()
    {
        TradingAccount account = Account();
        var segment = new PriceSegment(start: 100m, end: 120m, PricePhase.LowToHigh);
        Order newBuy = Submit(account, OrderSide.Buy, limit: 105m);
        Order newSell = Submit(account, OrderSide.Sell, limit: 115m);

        OrderMatcher.Next(segment, currentPrice: 110m, account, candleIndex: 0).ShouldBe(new OrderMatchCandidate(newSell.Id, pathPrice: 115m));
        account.Cancel(newSell.Id);
        OrderMatcher.Next(segment, currentPrice: 110m, account, candleIndex: 0).ShouldBeNull();
        newBuy.IsActive.ShouldBeTrue();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void MultipleLevels_MaintainMonotonicPointerAndStableTies(bool ascending)
    {
        TradingAccount account = Account();
        OrderSide side = ascending ? OrderSide.Sell : OrderSide.Buy;
        decimal[] submitted = ascending ? [119m, 104m, 115m, 104m, 111m] : [81m, 96m, 85m, 96m, 89m];

        foreach (decimal limit in submitted)
        {
            Submit(account, side, limit);
        }

        var selected = new List<decimal>();
        var ids = new List<long>();
        var current = 100m;
        PriceSegment segment = Segment(ascending);

        while (OrderMatcher.Next(segment, current, account, candleIndex: 0) is { } candidate)
        {
            (ascending ? candidate.PathPrice >= current : candidate.PathPrice <= current).ShouldBeTrue();
            selected.Add(candidate.PathPrice);
            ids.Add(candidate.OrderId);
            account.Execute(candidate.OrderId, candidate.PathPrice);
            current = candidate.PathPrice;
        }

        selected.ShouldBe(ascending ? new[] { 104m, 104m, 111m, 115m, 119m } : new[] { 96m, 96m, 89m, 85m, 81m });
        ids.ShouldBe(new[] { 3L, 5L, 6L, 4L, 2L });
        account.Portfolio.BaseReserved.ShouldBe(0m);
        account.Portfolio.QuoteReserved.ShouldBe(0m);
    }

    [TestCase(79)]
    [TestCase(121)]
    public void CurrentPointOutsideSegment_IsRejected(int current)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => OrderMatcher.Next(Segment(ascending: false), current, Account(), candleIndex: 0));
    }

    [TestCase(arg1: 0, arg2: 100)]
    [TestCase(arg1: 1, arg2: 0)]
    [TestCase(arg1: -1, arg2: 100)]
    [TestCase(arg1: 1, arg2: -1)]
    public void InvalidCandidateIdentityOrPathPrice_IsRejected(long orderId, int pathPrice)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new OrderMatchCandidate(orderId, pathPrice));
    }

    private static TradingAccount Account()
    {
        var settings = new ExecutionSettings(startingCapital: 100000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);
        var rules = new InstrumentRules(quantityStep: 0.01m, priceTick: 0.01m, minQuantity: 0m, minNotional: 0m, basePrecision: 2, quotePrecision: 2);
        var account = new TradingAccount(settings, rules);
        OrderSubmission seed = account.Submit(
            new OrderRequest(OrderSide.Buy, OrderType.Market, Amount: 1000m, LimitPrice: null, Tag: null, "seed", EligibleFromCandleIndex: 0));
        seed.IsAccepted.ShouldBeTrue();
        account.Execute(seed.Order!.Id, referencePrice: 100m).IsFilled.ShouldBeTrue();

        return account;
    }

    private static Order Submit(TradingAccount account, OrderSide side, decimal limit, int eligibleFrom = 0)
    {
        OrderSubmission submission = account.Submit(new OrderRequest(side, OrderType.Limit, side == OrderSide.Buy ? limit : 1m, limit, Tag: null, "limit", eligibleFrom));
        submission.IsAccepted.ShouldBeTrue();

        return submission.Order!;
    }

    private static PriceSegment Segment(bool ascending)
    {
        return ascending
            ? new PriceSegment(start: 100m, end: 120m, PricePhase.LowToHigh)
            : new PriceSegment(start: 100m, end: 80m, PricePhase.OpenToLow);
    }
}