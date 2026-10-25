using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

using static ProfitGraph.Tests.Unit.Engine.Replay.ContractFixtureTests;
using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Accounting;

/// <summary>
///     Bought items sell only after their settlement hold, partly when only some are unlocked (ADR 0022, C5).
/// </summary>
public sealed class SettlementHoldTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private static readonly TimeSpan Hold = TimeSpan.FromDays(7);

    private static readonly InstrumentRules WholeItems = new InstrumentRules(
        quantityStep: 1m,
        priceTick: 0.01m,
        minQuantity: 1m,
        minNotional: 0.01m,
        basePrecision: 0,
        quotePrecision: 2);

    [Test]
    public void SellsItemsAsTheyUnlockAndFillsTheOrderOnce()
    {
        TradingAccount account = Account();
        Buy(account, items: 1m, Start);
        Buy(account, items: 2m, Start.AddDays(1));
        Order sell = Submit(account, OrderSide.Sell, amount: 3m, price: 2m);

        account.AdvanceMarketTime(Start.Add(Hold).AddHours(-1));
        account.CanExecute(sell).ShouldBeFalse();

        account.AdvanceMarketTime(Start.Add(Hold));
        account.CanExecute(sell).ShouldBeTrue();
        Fill first = account.Execute(sell.Id, referencePrice: 0m).Fill!;

        first.GrossBase.ShouldBe(1m);
        first.QuoteChange.ShouldBe(2m);
        sell.IsActive.ShouldBeTrue();
        sell.Reservation.ShouldBe(2m);
        account.Portfolio.BaseTotal.ShouldBe(2m);
        account.Portfolio.BaseReserved.ShouldBe(2m);
        account.CanExecute(sell).ShouldBeFalse();

        account.AdvanceMarketTime(Start.AddDays(1).Add(Hold));
        Fill second = account.Execute(sell.Id, referencePrice: 0m).Fill!;

        second.GrossBase.ShouldBe(2m);
        sell.Status.ShouldBe(OrderStatus.Filled);
        account.Portfolio.BaseTotal.ShouldBe(0m);
        account.Portfolio.BaseReserved.ShouldBe(0m);
        account.Portfolio.QuoteTotal.ShouldBe(100m - 3m + 6m);
    }

    [Test]
    public void CancellingAPartlySoldOrderReleasesOnlyItsRemainder()
    {
        TradingAccount account = Account();
        Buy(account, items: 1m, Start);
        Buy(account, items: 1m, Start.AddDays(3));
        Order sell = Submit(account, OrderSide.Sell, amount: 2m, price: 2m);
        account.AdvanceMarketTime(Start.Add(Hold));
        account.Execute(sell.Id, referencePrice: 0m);

        account.Cancel(sell.Id);

        account.Portfolio.BaseTotal.ShouldBe(1m);
        account.Portfolio.BaseReserved.ShouldBe(0m);
    }

    [Test]
    public void WithoutAHoldAnItemSellsAtOnce()
    {
        var account = new TradingAccount(new ExecutionSettings(startingCapital: 100m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m), WholeItems);
        Buy(account, items: 1m, Start);
        Order sell = Submit(account, OrderSide.Sell, amount: 1m, price: 2m);

        account.CanExecute(sell).ShouldBeTrue();
        account.Execute(sell.Id, referencePrice: 0m).Fill!.GrossBase.ShouldBe(1m);
    }

    [Test]
    public void AHoldNeedsTheReplaysMarketTime()
    {
        TradingAccount account = Account();
        Order buy = Submit(account, OrderSide.Buy, amount: 1m, price: 1m);

        Should.Throw<TradingInvariantException>(() => account.Execute(buy.Id, referencePrice: 0m));
        account.AdvanceMarketTime(Start.AddDays(1));
        Should.Throw<TradingInvariantException>(() => account.AdvanceMarketTime(Start));
        Should.Throw<TradingInvariantException>(() => new ExecutionSettings(startingCapital: 1m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m)
            .WithSettlementHold(TimeSpan.FromSeconds(-1)));
    }

    [Test]
    public void ReplaySellsNothingBeforeTheHoldAndStaysDeterministic()
    {
        TimeSpan hold = TimeSpan.FromHours(3);
        BacktestResult held = new BacktestEngine().Run(Ladder(hold));
        BacktestResult again = new BacktestEngine().Run(Ladder(hold));
        BacktestResult free = new BacktestEngine().Run(Ladder(hold: null));

        held.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        held.Ledger.ShouldBe(again.Ledger);
        DateTimeOffset firstBuy = FillTimes(held, OrderSide.Buy).Min();
        IReadOnlyList<DateTimeOffset> sells = FillTimes(held, OrderSide.Sell);
        sells.ShouldNotBeEmpty("The ladder must sell for the comparison to mean anything.");
        sells.ShouldAllBe(time => time >= firstBuy + hold);
        FillTimes(free, OrderSide.Sell).Min().ShouldBeLessThan(sells.Min());
    }

    private static TradingAccount Account()
    {
        return new TradingAccount(new ExecutionSettings(startingCapital: 100m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m).WithSettlementHold(Hold), WholeItems);
    }

    private static void Buy(TradingAccount account, decimal items, DateTimeOffset at)
    {
        account.AdvanceMarketTime(at);
        Order order = Submit(account, OrderSide.Buy, items, price: 1m);
        account.Execute(order.Id, referencePrice: 0m).Fill.ShouldNotBeNull();
    }

    private static Order Submit(TradingAccount account, OrderSide side, decimal amount, decimal price)
    {
        OrderSubmission submission = account.Submit(new OrderRequest(side, OrderType.Limit, amount, price, Tag: null, "node", EligibleFromCandleIndex: 0));
        submission.Rejection.ShouldBeNull();

        return submission.Order!;
    }

    private static BacktestInput Ladder(TimeSpan? hold)
    {
        CompiledTradingGraph graph = new TradingGraphCompiler()
            .Compile(LadderStrategyGraphGenerator.Generate(), Catalog)
            .Graph.ShouldNotBeNull();
        var settings = new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);

        return new BacktestInput(
            graph,
            EngineExecutionInputs.Empty,
            hold is { } duration ? settings.WithSettlementHold(duration) : settings,
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 6),
            new PreparedReplay(
                Timeframe.Parse("1h"),
                Enumerable.Range(start: 0, count: 10).Select(index => Bar(index)),
                Enumerable.Range(start: 10, count: 40).Select(index => Bar(index, low: 85m, high: 110m, close: 105m))));
    }

    private static IReadOnlyList<DateTimeOffset> FillTimes(BacktestResult result, OrderSide side)
    {
        return
        [
            .. result.Ledger
                .Where(entry => entry.Kind == BacktestLedgerKind.Filled && entry.Fill!.Side == side)
                .Select(entry => entry.Context.Phase == PricePhase.Open ? entry.Context.Candle.OpenTime : entry.Context.CandleCloseTime)
        ];
    }
}