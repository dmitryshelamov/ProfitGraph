using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Grids;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Templates;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Grids;

/// <summary>
///     A grid on an externally executed account, as a live bot runs it (ADR 0058, section 10): pending submits, a buy cancelled
///     after part of it filled, the owner's cancel and a checkpoint restart.
/// </summary>
public sealed class GridExternalTests
{
    private static readonly Candle Bar = new Candle(
        new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero),
        Open: 100m,
        High: 100m,
        Low: 100m,
        Close: 100m,
        Volume: 1m);

    private static readonly ExecutionSettings Settings = new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m);

    private static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 0.0001m,
        priceTick: 0.01m,
        minQuantity: 0m,
        minNotional: 1m,
        basePrecision: 4,
        quotePrecision: 2);

    [Test]
    public void TheStartPlacesTheLevelsBuysAsPendingSubmits()
    {
        (CausalDispatcher _, ExternallyExecutedAccount account) = Started();

        account.Capture()
            .Orders.Select(order => $"{order.Tag} {order.Status}")
            .ShouldBe(["grid.grid.1.buy PendingSubmit", "grid.grid.2.buy PendingSubmit"]);
    }

    [Test]
    public void ABuyCancelledAfterAPartialFillSellsWhatItBought()
    {
        (CausalDispatcher dispatcher, ExternallyExecutedAccount account) = Started();
        Apply(dispatcher, ExternalOrderUpdate.Accept(orderId: 1));
        Apply(dispatcher, ExternalOrderUpdate.Partial(orderId: 1, grossBase: 0.5m, grossQuote: 49.5m, fee: 0m));

        dispatcher.RequestOrderCancel(orderId: 1).ShouldBe(ExternalCancelDisposition.Pending);
        dispatcher.Drain();
        Apply(dispatcher, ExternalOrderUpdate.ConfirmCancel(orderId: 1));

        OrderSnapshot sale = account.Capture().Orders.Where(order => order.Tag == "grid.grid.1.sell").ShouldHaveSingleItem();
        sale.Quantity.ShouldBe(0.5m);
        sale.LimitPrice.ShouldBe(99.99m);
        account.Capture().Orders.ShouldNotContain(order => order.Tag == "grid.grid.1.buy");
    }

    [Test]
    public void AnOwnersCancelOfAnUnfilledBuyIsPlacedAgain()
    {
        (CausalDispatcher dispatcher, ExternallyExecutedAccount account) = Started();
        Apply(dispatcher, ExternalOrderUpdate.Accept(orderId: 2));

        dispatcher.RequestOrderCancel(orderId: 2);
        dispatcher.Drain();
        Apply(dispatcher, ExternalOrderUpdate.ConfirmCancel(orderId: 2));

        OrderSnapshot again = account.Capture().Orders.Where(order => order.Tag == "grid.grid.2.buy").ShouldHaveSingleItem();
        again.Id.ShouldNotBe(2);
        again.LimitPrice.ShouldBe(98.01m);
    }

    [Test]
    public void ARestartedEngineKeepsTheLevelsAndBuysAgainAfterTheSale()
    {
        (CausalDispatcher dispatcher, _) = Started();
        Apply(dispatcher, ExternalOrderUpdate.Accept(orderId: 1));
        Apply(dispatcher, ExternalOrderUpdate.Final(orderId: 1, grossBase: 1.0101m, grossQuote: 99.9999m, fee: 0m));
        GraphEngineCheckpoint checkpoint = dispatcher.CaptureCheckpoint("v1", Bar.OpenTime.AddHours(1));

        checkpoint.Grids.ShouldHaveSingleItem()
            .Levels.ShouldHaveSingleItem()
            .ShouldBe(new GridLevelCheckpoint(Index: 1, HeldBase: 1.0101m, PaidQuote: 99.9999m, Blocked: false));
        CausalDispatcher restored = CausalDispatcher.Restore(checkpoint, Compile(), Settings, Rules, [Bar]);
        restored.Drain();
        var account = (ExternallyExecutedAccount)restored.Account;
        long saleId = account.Capture().Orders.Single(order => order.Tag == "grid.grid.1.sell").Id;
        Apply(restored, ExternalOrderUpdate.Accept(saleId));
        Apply(restored, ExternalOrderUpdate.Final(saleId, grossBase: 1.0101m, grossQuote: 101.0001m, fee: 0m));

        account.Capture().Orders.Select(order => order.Tag).Order().ShouldBe(["grid.grid.1.buy", "grid.grid.2.buy"]);
        restored.CaptureCheckpoint("v1", Bar.OpenTime.AddHours(1)).Grids.ShouldHaveSingleItem().Levels.ShouldBeEmpty();
    }

    private static (CausalDispatcher Dispatcher, ExternallyExecutedAccount Account) Started()
    {
        var account = new ExternallyExecutedAccount(Settings, Rules);
        var dispatcher = new CausalDispatcher(Compile(), account, graphVersionId: "v1");
        dispatcher.StartAndDrain(new ReachedMarketContext(Bar, candleIndex: 0, Bar.OpenTime.AddHours(1), PricePhase.Open, Bar.Open, eligibleFromCandleIndex: 0));
        dispatcher.AdvanceClosedCandle(Bar, historyIndex: 0);

        return (dispatcher, account);
    }

    private static CompiledTradingGraph Compile()
    {
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(GridStrategyBlockFactory.Create(levels: 2));
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(lowered.Graph, Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();

        return compilation.Graph.ShouldNotBeNull();
    }

    private static void Apply(CausalDispatcher dispatcher, ExternalOrderUpdate update)
    {
        dispatcher.ApplyExternalOrderUpdate(
            update,
            new ReachedMarketContext(Bar, candleIndex: 0, Bar.OpenTime.AddHours(1), PricePhase.External, marketPrice: 100m, eligibleFromCandleIndex: 1));
        dispatcher.Drain();
    }
}