using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Templates;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

/// <summary>
///     A live level trade on a venue (ADR 0060) whose entry filled while the last close was already below the stop, as a GROVEUSDT bot
///     on Bybit did: no target is placed, and the next close at or below the stop must still sell the whole position at market. The
///     position check before the stop used to stay unavailable for a sellable position, so the stop never ran.
/// </summary>
public sealed class LevelTradeStopDispatchTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 10, day: 30, hour: 2, minute: 21, second: 0, TimeSpan.Zero);

    private static readonly InstrumentRules Rules = new InstrumentRules(
        quantityStep: 0.1m,
        priceTick: 0.000001m,
        minQuantity: 0.1m,
        minNotional: 5m,
        basePrecision: 1,
        quotePrecision: 7);

    [Test]
    public void ACloseBelowTheStopSellsAPositionThatHasNoTarget()
    {
        (CausalDispatcher dispatcher, ExternallyExecutedAccount account) = Filled();

        Close(dispatcher, minute: 1, close: 0.007981m);

        OrderSnapshot stop = account.Capture().Orders.ShouldHaveSingleItem();
        stop.Tag.ShouldBe(LevelTradeStrategyBlockFactory.StopTag);
        stop.Side.ShouldBe(OrderSide.Sell);
        stop.Type.ShouldBe(OrderType.Market);
        stop.Quantity.ShouldBe(3643.9m);
    }

    [Test]
    public void ACloseAboveTheStopKeepsThePosition()
    {
        (CausalDispatcher dispatcher, ExternallyExecutedAccount account) = Filled();

        Close(dispatcher, minute: 1, close: 0.008359m);

        account.Capture().Orders.ShouldBeEmpty();
        account.Capture().BaseTotal.ShouldBe(3643.9291m);
    }

    /// <summary>
    ///     The entry of 30 USDT at 0.008218 filled at 0.008047 with the fee in GROVE, while the last close of 0.007958 was below the
    ///     stop of 0.00813725.
    /// </summary>
    private static (CausalDispatcher Dispatcher, ExternallyExecutedAccount Account) Filled()
    {
        StrategyDefinition definition = LevelTradeStrategyBlockFactory.Create(buyAmount: 30m, entryPrice: 0.008218m, stopPrice: 0.00813725m, targetPrice: 0.0083795m);
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(definition);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(lowered.Graph, Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        var account = new ExternallyExecutedAccount(new ExecutionSettings(startingCapital: 30m, slippage: 0m, limitFeeRate: 0.0018m, marketFeeRate: 0.0018m), Rules);
        var dispatcher = new CausalDispatcher(compilation.Graph.ShouldNotBeNull(), account, graphVersionId: "v1");
        Candle first = Bar(minute: 0, close: 0.007958m);
        dispatcher.StartAndDrain(new ReachedMarketContext(first, candleIndex: 0, first.OpenTime.AddMinutes(1), PricePhase.Open, first.Open, eligibleFromCandleIndex: 0));
        dispatcher.AdvanceClosedCandle(first, historyIndex: 0);
        var external = new ReachedMarketContext(first, candleIndex: 0, first.OpenTime.AddMinutes(1), PricePhase.External, first.Close, eligibleFromCandleIndex: 1);
        dispatcher.ApplyExternalOrderUpdate(ExternalOrderUpdate.Accept(orderId: 1), external);
        dispatcher.Drain();
        dispatcher.ApplyExternalOrderUpdate(ExternalOrderUpdate.Final(orderId: 1, grossBase: 3650.5m, grossQuote: 29.3757838m, fee: 6.5709m), external);
        dispatcher.Drain();
        account.Capture().Orders.ShouldBeEmpty();

        return (dispatcher, account);
    }

    /// <summary>
    ///     A live closed candle as the bot's session processes one: its open, then its close and the candle-closed event.
    /// </summary>
    private static void Close(CausalDispatcher dispatcher, int minute, decimal close)
    {
        Candle candle = Bar(minute, close);
        DateTimeOffset closeTime = candle.OpenTime.AddMinutes(1);
        dispatcher.BeginOpen(new ReachedMarketContext(candle, minute, closeTime, PricePhase.Open, candle.Open, minute));
        dispatcher.AdvanceClosedCandle(candle, minute);
        var context = new ReachedMarketContext(candle, minute, closeTime, PricePhase.Close, candle.Close, minute + 1);
        dispatcher.BeginClose(context);
        dispatcher.EnqueueExternal(EventKind.CandleClosed, new CandlePayload(candle), context);
        dispatcher.Drain();
    }

    private static Candle Bar(int minute, decimal close)
    {
        return new Candle(Start.AddMinutes(minute), close, close, close, close, Volume: 1000m);
    }
}