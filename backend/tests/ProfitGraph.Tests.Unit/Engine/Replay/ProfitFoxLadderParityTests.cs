using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class ProfitFoxLadderParityTests
{
    private static readonly IReadOnlyList<INodeDefinition> Definitions = EngineGraphFixture.Definitions
        .Concat(Enum.GetValues<TradingNodeBehavior>().Select(behavior => new TradingNodeDefinition(behavior)))
        .ToArray();

    [Test]
    public void TwoBelowMinimumAveragesMatchEveryIndependentDecisionAndFillAtTheExactReplayPhase()
    {
        InstrumentRules rules = Rules(step: 0.1m);
        Candle[] candles = [Bar(open: 100m, low: 80m, high: 100m, close: 85m)];
        var model = new ProfitFoxLadderReferenceModel(rules, buyAmount: 5m, allocationCap: 100m, rise: 1m, drop: 0.1m);
        BacktestResult result = Compare(model, candles, rules, allocationCap: 100m);
        Fill[] buys = result.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled).Select(entry => entry.Fill!).ToArray();
        buys.Select(fill => fill.Price).ShouldBe(new[] { 100m, 90m, 81m });
        buys.Select(fill => fill.GrossBase).ShouldBe(new[] { 0.1m, 0.2m, 0.2m });
        model.Orders.Count.ShouldBe(7);
        model.Cost.ShouldBe(44.2m);
        model.Base.ShouldBe(0.5m);
        model.Orders.Where(order => order.Tag == "ladder.exit").Select(order => order.Price).ShouldBe(new decimal?[] { 200m, 186.6m, 176.8m });
        model.Orders.Where(order => order.Tag == "ladder.average").ShouldAllBe(order => order.Quantity * order.Price > 5m);
    }

    [Test]
    public void RaisedAverageThatExceedsRemainingDepositIsSkippedRatherThanCut()
    {
        InstrumentRules rules = Rules(step: 0.1m);
        Candle[] candles = [Bar(open: 100m, low: 80m, high: 100m, close: 85m)];
        var model = new ProfitFoxLadderReferenceModel(rules, buyAmount: 5m, allocationCap: 35m, rise: 1m, drop: 0.1m);
        BacktestResult result = Compare(model, candles, rules, allocationCap: 35m);
        model.RaisedAverageBlockedByDeposit.ShouldBeTrue();
        model.Cost.ShouldBe(28m);
        model.Base.ShouldBe(0.3m);
        (35m - model.Cost).ShouldBeGreaterThan(5m);
        (0.2m * 81m).ShouldBeGreaterThan(35m - model.Cost);
        model.Orders.Count.ShouldBe(4);
        result.Ledger.ShouldNotContain(entry => entry.Kind == BacktestLedgerKind.Submitted && entry.Order!.LimitPrice == 81m);
    }

    [Test]
    public void ExitRetainsDustAndDesignerDeclinesTheSourceWholeDepositRestart()
    {
        InstrumentRules rules = Rules(step: 0.01m, tick: 0.01m);
        Candle[] candles = [Bar(open: 100m, low: 100m, high: 203m, close: 200m)];
        var model = new ProfitFoxLadderReferenceModel(rules, buyAmount: 5m, allocationCap: 10m, rise: 1m, drop: 0.1m, feeRate: 0.01m);
        BacktestResult result = Compare(model, candles, rules, allocationCap: 10m, fee: 0.01m);
        model.Orders.Count.ShouldBe(2);
        model.Base.ShouldBe(0.009m);
        model.Cost.ShouldBe(10m - 10m * 0.09m / 0.099m);
        model.Orders.Last().Quantity.ShouldBe(0.09m);
        model.Orders.Last().Price.ShouldBe(202.02m);
        model.RetainedCostBlockedEntry.ShouldBeTrue();

        // ADR 0062 accepted difference: source entry checks total deposit; Designer includes retained dust cost.
        model.SourceEntryBudget.ShouldBe(10m);
        model.SourceEntryBudget.ShouldBeLessThanOrEqualTo(10m);
        model.DesignerEntryBudget.ShouldBeGreaterThan(10m - model.Cost);
        result.Terminal.Account.Orders.ShouldBeEmpty();
    }

    [Test]
    public void UnrepresentableSourceQuoteMinimumIsCeiledWithoutChangingTheSourceRule()
    {
        InstrumentRules rules = Rules(step: 0.000001m, tick: 0.01m, minNotional: 5.001m, quotePrecision: 2);
        Candle[] candles = [Bar(open: 100m, low: 100m, high: 100m, close: 100m)];
        var model = new ProfitFoxLadderReferenceModel(rules, buyAmount: 4m, allocationCap: 100m, rise: 1m, drop: 0.1m);
        Compare(model, candles, rules, allocationCap: 100m, buyAmount: 4m);

        // ADR 0062 representation difference: source sends exact quote budget; Designer must represent its quote unit.
        model.SourceEntryBudget.ShouldBe(5.001m);
        model.DesignerEntryBudget.ShouldBe(5.01m);
        model.Orders.First().QuoteBudget.ShouldBe(5.01m);
    }

    [Test]
    public void PositiveExitBelowMinimumKeepsExistingDesignerDustBehaviorRatherThanSourceDegradation()
    {
        InstrumentRules rules = Rules(step: 0.01m, tick: 0.01m);
        Candle[] candles = [Bar(open: 100m, low: 100m, high: 100m, close: 100m)];
        var model = new ProfitFoxLadderReferenceModel(rules, buyAmount: 5m, allocationCap: 10m, rise: 0.01m, drop: 0.1m, feeRate: 0.01m);
        BacktestResult result = Compare(model, candles, rules, allocationCap: 10m, fee: 0.01m, rise: 0.01m);

        // ADR 0062 accepted difference: source degrades OrderPlacementFailed; Designer's existing sell guard declines.
        model.SourceWouldDegradePositiveExit.ShouldBeTrue();
        model.Orders.Count.ShouldBe(1);
        model.Base.ShouldBe(0.099m);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Terminal.Account.Orders.ShouldBeEmpty();
    }

    private static BacktestResult Compare(
        ProfitFoxLadderReferenceModel model,
        Candle[] candles,
        InstrumentRules rules,
        decimal allocationCap,
        decimal fee = 0m,
        decimal buyAmount = 5m,
        decimal rise = 1m)
    {
        model.Run(candles);
        StrategyDefinition definition = ProfitFoxLadderStrategyBlockFactory.Create(buyAmount, allocationCap, drop: 0.1m, rise);
        StrategyGraph graph = new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog).Lower(definition).Graph;
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, Definitions);
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        var input = new BacktestInput(
            compilation.Graph.ShouldNotBeNull(),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 100m, slippage: 0m, fee, fee),
            rules,
            Timeframe.Parse("1h"),
            Array.Empty<Candle>(),
            candles);
        BacktestResult result = new BacktestEngine().Run(input);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        ProfitFoxReferenceOrder[] actualOrders = result.Ledger
            .Where(entry => entry.Kind == BacktestLedgerKind.Submitted)
            .Select(entry => new ProfitFoxReferenceOrder(
                entry.Order!.Side,
                entry.Order.Type,
                entry.Order.Quantity,
                entry.Order.LimitPrice,
                entry.Order.QuoteBudget,
                entry.Order.Tag!,
                entry.Context.CandleIndex,
                entry.Context.Phase,
                entry.Context.MarketPrice))
            .ToArray();
        actualOrders.ShouldBe(model.Orders);
        result.Ledger
            .Where(entry => entry.Kind == BacktestLedgerKind.Filled)
            .Select(entry => (entry.Fill!.Side, entry.Fill.Price, entry.Fill.GrossBase, entry.Context.CandleIndex, entry.Context.Phase))
            .ShouldBe(model.Fills);
        result.Terminal.Account.BaseTotal.ShouldBe(model.Base);
        result.Terminal.Account.CostBasis.ShouldBe(model.Cost);

        return result;
    }

    private static InstrumentRules Rules(decimal step, decimal tick = 0.1m, decimal minNotional = 10m, int quotePrecision = 8)
    {
        return new InstrumentRules(step, tick, minQuantity: 0m, minNotional, basePrecision: 6, quotePrecision);
    }

    private static Candle Bar(decimal open, decimal low, decimal high, decimal close)
    {
        return new Candle(DateTimeOffset.UnixEpoch, open, high, low, close, Volume: 1m);
    }
}