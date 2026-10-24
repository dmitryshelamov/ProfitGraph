using System.Text.Json;
using System.Text.Json.Serialization;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Application.Strategies;

/// <summary>
///     The Ladder SMA template replays exactly like the version saved by a user in the archive application.
/// </summary>
public sealed class LadderSmaTemplateTests
{
    [TestCase(0)]
    [TestCase(0.001)]
    public void FullCycle_MatchesSavedSubmissionsFillsCancellationsFeesAndFinalAccount(decimal fee)
    {
        BacktestResult result = ReplayAndCompare(
            [Bar(index: 0, open: 100m, low: 85m, high: 100m, close: 95m), Bar(index: 1, open: 100m, low: 100m, high: 106m, close: 100m)],
            warmupCount: 50,
            fee);
        BacktestLedgerEntry[] fills = Facts(result, BacktestLedgerKind.Filled);
        fills.Select(entry => entry.Order!.Tag).ShouldBe(["ladder.entry", "ladder.average", "ladder.exit", "ladder.entry"]);
        fills[1].Fill!.Price.ShouldBe(90m);
        fills[2].Fill!.Side.ShouldBe(OrderSide.Sell);
        result.Terminal.Account.RealizedPnl.ShouldBeGreaterThan(0m);
        Facts(result, BacktestLedgerKind.Cancelled).ShouldContain(entry => entry.Order!.Tag == "ladder.exit");
        Facts(result, BacktestLedgerKind.Cancelled).ShouldContain(entry => entry.Order!.Tag == "ladder.average");

        if (fee > 0m)
        {
            result.Terminal.Account.FeesBase.ShouldBeGreaterThan(0m);
            result.Terminal.Account.FeesQuote.ShouldBeGreaterThan(0m);
        }
    }

    [Test]
    public void SmaUnavailableAtFirstOpen_EntryAndExitProceedAndCloseRecheckEventuallyAverages()
    {
        Candle[] candles =
        [
            .. Enumerable.Range(start: 0, count: 50).Select(index => Bar(index, open: 100m, low: 100m, high: 100m, close: 100m)),
            Bar(index: 50, open: 100m, low: 85m, high: 100m, close: 95m)
        ];
        BacktestResult result = ReplayAndCompare(candles, warmupCount: 0, fee: 0.001m);
        BacktestLedgerEntry[] submissions = Facts(result, BacktestLedgerKind.Submitted);
        submissions.Single(entry => entry.Order!.Tag == "ladder.entry").Context.CandleIndex.ShouldBe(0);
        submissions.First(entry => entry.Order!.Tag == "ladder.exit").Context.CandleIndex.ShouldBe(0);
        BacktestLedgerEntry average = submissions.First(entry => entry.Order!.Tag == "ladder.average");
        average.Context.CandleIndex.ShouldBe(49);
        average.Context.Phase.ShouldBe(PricePhase.Close);
        average.Order!.EligibleFromCandleIndex.ShouldBe(50);
        average.Order.LimitPrice.ShouldBe(90m);
        BacktestLedgerEntry fill = Facts(result, BacktestLedgerKind.Filled).Single(entry => entry.Order!.Tag == "ladder.average");
        fill.Context.CandleIndex.ShouldBe(50);
        fill.Fill!.Price.ShouldBe(90m);
    }

    [Test]
    public void OpeningGapThroughAverage_MatchesSavedAndFillsAtRestingLimitWithoutPriceImprovement()
    {
        BacktestResult result = ReplayAndCompare(
            [Bar(index: 0, open: 100m, low: 100m, high: 100m, close: 100m), Bar(index: 1, open: 80m, low: 80m, high: 85m, close: 82m)],
            warmupCount: 50,
            fee: 0.001m);
        BacktestLedgerEntry submitted = Facts(result, BacktestLedgerKind.Submitted).First(entry => entry.Order!.Tag == "ladder.average");
        BacktestLedgerEntry fill = Facts(result, BacktestLedgerKind.Filled).Single(entry => entry.Order!.Tag == "ladder.average");
        submitted.Context.CandleIndex.ShouldBe(0);
        submitted.Order!.LimitPrice.ShouldBe(90m);
        fill.Context.CandleIndex.ShouldBe(1);
        fill.Context.Phase.ShouldBe(PricePhase.Open);
        fill.Fill!.Price.ShouldBe(90m);
        Facts(result, BacktestLedgerKind.Cancelled).ShouldContain(entry => entry.Order!.Tag == "ladder.exit");
    }

    private static BacktestResult ReplayAndCompare(Candle[] candles, int warmupCount, decimal fee)
    {
        StrategyDefinition saved = LoadSavedDefinition();
        StrategyDefinition template = LadderSmaStrategyBlockFactory.Create();
        template.Parameters.Select(parameter => parameter with { DisplayUnit = null }).ShouldBe(saved.Parameters);
        BacktestResult expected = Replay(saved, candles, warmupCount, fee);
        BacktestResult actual = Replay(template, candles, warmupCount, fee);
        expected.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        actual.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        actual.Ledger.ShouldBe(expected.Ledger);
        actual.Diagnostics.ShouldBe(expected.Diagnostics);
        actual.OrderCount.ShouldBe(expected.OrderCount);
        actual.FillCount.ShouldBe(expected.FillCount);
        actual.Terminal.ProcessedCandles.ShouldBe(expected.Terminal.ProcessedCandles);
        actual.Terminal.Context.ShouldBe(expected.Terminal.Context);
        actual.Terminal.Equity.ShouldBe(expected.Terminal.Equity);
        actual.Terminal.TotalPnl.ShouldBe(expected.Terminal.TotalPnl);
        actual.Terminal.UnrealizedPnl.ShouldBe(expected.Terminal.UnrealizedPnl);
        AccountSnapshot expectedAccount = expected.Terminal.Account;
        AccountSnapshot actualAccount = actual.Terminal.Account;
        actualAccount.StartingCapital.ShouldBe(expectedAccount.StartingCapital);
        actualAccount.QuoteTotal.ShouldBe(expectedAccount.QuoteTotal);
        actualAccount.QuoteReserved.ShouldBe(expectedAccount.QuoteReserved);
        actualAccount.BaseTotal.ShouldBe(expectedAccount.BaseTotal);
        actualAccount.BaseReserved.ShouldBe(expectedAccount.BaseReserved);
        actualAccount.CostBasis.ShouldBe(expectedAccount.CostBasis);
        actualAccount.RealizedPnl.ShouldBe(expectedAccount.RealizedPnl);
        actualAccount.FeesBase.ShouldBe(expectedAccount.FeesBase);
        actualAccount.FeesQuote.ShouldBe(expectedAccount.FeesQuote);
        actualAccount.LastBuyPrice.ShouldBe(expectedAccount.LastBuyPrice);
        actualAccount.Orders.ShouldBe(expectedAccount.Orders);

        return actual;
    }

    private static StrategyDefinition LoadSavedDefinition()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Strategies", "ladder-sma-v2-version-2.json");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        return JsonSerializer.Deserialize<StrategyDefinitionInternal>(File.ReadAllText(path), options).ShouldNotBeNull().ToDomain();
    }

    private static CompiledTradingGraph Compile(StrategyDefinition definition)
    {
        new StrategyDefinitionValidator().Validate(definition, ProductionTradingNodeTests.Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog).Lower(definition);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(lowered.Graph, ProductionTradingNodeTests.Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();

        return compilation.Graph.ShouldNotBeNull();
    }

    private static BacktestResult Replay(StrategyDefinition definition, Candle[] candles, int warmupCount, decimal fee)
    {
        var input = new BacktestInput(
            Compile(definition),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, fee, fee),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("1h"),
            Enumerable.Range(-warmupCount, warmupCount).Select(index => Bar(index, open: 100m, low: 100m, high: 100m, close: 100m)),
            candles);

        return new BacktestEngine().Run(input);
    }

    private static BacktestLedgerEntry[] Facts(BacktestResult result, BacktestLedgerKind kind)
    {
        return result.Ledger.Where(entry => entry.Kind == kind).ToArray();
    }

    private static Candle Bar(int index, decimal open, decimal low, decimal high, decimal close)
    {
        return new Candle(DateTimeOffset.UnixEpoch.AddHours(index), open, high, low, close, Volume: 1m);
    }
}