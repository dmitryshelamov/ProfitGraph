using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

/// <summary>
///     The short level trade of a scanner trade card (ADR 0061): a limit sale at the entry, a limit cover at the target and a market
///     cover after a close at or above the stop, once per run. Entry 100, stop 105, target 90, 1 000 sold, quote fees of 0.1 %.
/// </summary>
public sealed class LevelTradeShortTemplateTests
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(year: 2026, month: 1, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void TheTargetCoversWhatTheEntrySoldAndTheTradeDoesNotRepeat()
    {
        Candle[] range =
        [
            Flat(index: 0, price: 98m), Bar(index: 1, open: 98m, high: 101m, low: 98m, close: 99m), Bar(index: 2, open: 99m, high: 99m, low: 89m, close: 92m),
            Bar(index: 3, open: 92m, high: 101m, low: 92m, close: 100m), Flat(index: 4, price: 100m)
        ];

        BacktestResult result = Run(range);

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Diagnostics.ShouldBeEmpty();
        Fills(result).ShouldBe([(LevelTradeStrategyBlockFactory.EntryTag, OrderSide.Sell, 100m), (LevelTradeStrategyBlockFactory.TargetTag, OrderSide.Buy, 90m)]);

        // 10 sold at 100 less 1 of fee, 10 bought back at 90 plus 0.9 of fee.
        result.Ledger.Last(entry => entry.Kind == BacktestLedgerKind.Filled).Fill!.RealizedPnl.ShouldBe(98.1m);
        result.Terminal.Account.BaseTotal.ShouldBe(0m);
        result.Terminal.Account.QuoteTotal.ShouldBe(10098.1m);
    }

    [Test]
    public void ACloseAtTheStopCoversAtTheNextOpenAndCancelsTheTarget()
    {
        Candle[] range =
        [
            Flat(index: 0, price: 98m), Bar(index: 1, open: 98m, high: 101m, low: 98m, close: 99m), Bar(index: 2, open: 99m, high: 106m, low: 99m, close: 105m),
            Bar(index: 3, open: 106m, high: 107m, low: 106m, close: 107m), Bar(index: 4, open: 107m, high: 107m, low: 85m, close: 99m), Flat(index: 5, price: 99m)
        ];

        BacktestResult result = Run(range);

        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        Fills(result).ShouldBe([(LevelTradeStrategyBlockFactory.EntryTag, OrderSide.Sell, 100m), (LevelTradeStrategyBlockFactory.StopTag, OrderSide.Buy, 106m)]);
        result.Terminal.Account.BaseTotal.ShouldBe(0m);
    }

    [Test]
    public void AHighAboveTheStopWithACloseBelowItKeepsTheShort()
    {
        Candle[] range =
        [
            Flat(index: 0, price: 98m), Bar(index: 1, open: 98m, high: 101m, low: 98m, close: 99m), Bar(index: 2, open: 99m, high: 110m, low: 99m, close: 104m),
            Flat(index: 3, price: 104m)
        ];

        BacktestResult result = Run(range);

        Fills(result).ShouldBe([(LevelTradeStrategyBlockFactory.EntryTag, OrderSide.Sell, 100m)]);
        result.Terminal.Account.BaseTotal.ShouldBe(-10m);
    }

    [Test]
    public void AnUnreachedEntryTradesNothing()
    {
        Candle[] range = [Flat(index: 0, price: 98m), Bar(index: 1, open: 98m, high: 99m, low: 88m, close: 90m), Flat(index: 2, price: 95m)];

        Fills(Run(range)).ShouldBeEmpty();
    }

    [Test]
    public void TheEntryNeedsItsMarginAtTheLeverage()
    {
        Candle[] range = [Flat(index: 0, price: 98m), Bar(index: 1, open: 98m, high: 101m, low: 98m, close: 99m), Flat(index: 2, price: 99m)];

        // Selling 1 000 at 2× needs 500 of margin and 1 of fee; 400 is not enough, 501 is.
        Fills(Run(range, capital: 400m, leverage: 2m)).ShouldBeEmpty();
        Fills(Run(range, capital: 501m, leverage: 2m)).ShouldBe([(LevelTradeStrategyBlockFactory.EntryTag, OrderSide.Sell, 100m)]);
    }

    [Test]
    public void AShortStrategyCompilesToAShortPlan()
    {
        Compile(LevelTradeShortStrategyBlockFactory.Create()).Direction.ShouldBe(PositionDirection.Short);
        Compile(LevelTradeStrategyBlockFactory.Create()).Direction.ShouldBe(PositionDirection.Long);
    }

    private static (string Tag, OrderSide Side, decimal Price)[] Fills(BacktestResult result)
    {
        return
        [
            .. result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled)
                .Select(entry => (entry.Fill!.Tag!, entry.Fill.Side, entry.Fill.Price))
        ];
    }

    private static BacktestResult Run(IEnumerable<Candle> range, decimal capital = 10000m, decimal leverage = 1m)
    {
        var input = new BacktestInput(
            Compile(LevelTradeShortStrategyBlockFactory.Create(sellAmount: 1000m)),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(capital, slippage: 0m, limitFeeRate: 0.001m, marketFeeRate: 0.001m).WithMargin(leverage),
            new InstrumentRules(
                quantityStep: 0.00000001m,
                priceTick: 0.00000001m,
                minQuantity: 0m,
                minNotional: 0m,
                basePrecision: 8,
                quotePrecision: 8,
                FeeAsset.Quote,
                exactQuoteFees: true),
            Timeframe.Parse("1m"),
            [],
            range);

        return new BacktestEngine().Run(input);
    }

    private static CompiledTradingGraph Compile(StrategyDefinition definition)
    {
        new StrategyDefinitionValidator().Validate(definition, ProductionTradingNodeTests.Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog).Lower(definition);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(lowered.Graph, ProductionTradingNodeTests.Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();

        return compilation.Graph.ShouldNotBeNull();
    }

    private static Candle Flat(int index, decimal price)
    {
        return Bar(index, price, price, price, price);
    }

    private static Candle Bar(int index, decimal open, decimal high, decimal low, decimal close)
    {
        return new Candle(Start.AddMinutes(index), open, high, low, close, Volume: 10m);
    }
}
