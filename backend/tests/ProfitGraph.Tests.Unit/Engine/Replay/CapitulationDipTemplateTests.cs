using System.Globalization;

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

public sealed class CapitulationDipTemplateTests
{
    [TestCase("kasusdt", arg2: 5)]
    [TestCase("wifusdt", arg2: 8)]
    public void DefaultTemplate_MatchesReferenceSignalsAndFills(string symbol, int expectedTradeCount)
    {
        Candle[] candles = ReadRows($"{symbol}-4h-20251002-20261012.csv")
            .Select(cells => new Candle(
                ParseTime(cells[0]),
                ParseNumber(cells[1]),
                ParseNumber(cells[2]),
                ParseNumber(cells[3]),
                ParseNumber(cells[4]),
                ParseNumber(cells[5])))
            .ToArray();
        candles[60].OpenTime.ShouldBe(ParseTime("2025-10-12T00:00:00Z"));
        string[][] expected = ReadRows($"{symbol}-4h-expected-trades.csv");
        expected.Length.ShouldBe(expectedTradeCount);
        var input = new BacktestInput(
            Compile(CapitulationDipStrategyBlockFactory.Create()),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 10000m, slippage: 0m, limitFeeRate: 0m, marketFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.00000001m, priceTick: 0.00000001m, minQuantity: 0m, minNotional: 0m, basePrecision: 8, quotePrecision: 8),
            Timeframe.Parse("4h"),
            candles.Take(60),
            candles.Skip(60));
        BacktestResult result = new BacktestEngine().Run(input);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Diagnostics.ShouldBeEmpty();
        BacktestLedgerEntry[] fills = result.Ledger
            .Where(entry => entry.Kind == BacktestLedgerKind.Filled)
            .ToArray();
        BacktestLedgerEntry[] submissions = result.Ledger
            .Where(entry => entry.Kind == BacktestLedgerKind.Submitted)
            .ToArray();
        fills.Length.ShouldBe(expectedTradeCount * 2);
        submissions.Length.ShouldBe(fills.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            string[] trade = expected[index];
            BacktestLedgerEntry entry = fills[index * 2];
            BacktestLedgerEntry exit = fills[index * 2 + 1];
            BacktestLedgerEntry entrySignal = submissions.Single(item => item.Order!.Id == entry.Order!.Id);
            BacktestLedgerEntry exitSignal = submissions.Single(item => item.Order!.Id == exit.Order!.Id);
            entry.Fill!.Side.ShouldBe(OrderSide.Buy);
            exit.Fill!.Side.ShouldBe(OrderSide.Sell);
            entry.Fill.Tag.ShouldBe("capitulation.entry");
            exit.Fill.Tag.ShouldBe("capitulation.exit");
            entrySignal.Context.Candle.OpenTime.ShouldBe(ParseTime(trade[0]));
            entrySignal.Context.Phase.ShouldBe(PricePhase.Close);
            entry.Context.Candle.OpenTime.ShouldBe(ParseTime(trade[1]));
            entry.Context.Phase.ShouldBe(PricePhase.Open);
            entry.Fill.Price.ShouldBe(ParseNumber(trade[2]));
            exitSignal.Context.Candle.OpenTime.ShouldBe(ParseTime(trade[3]));
            exitSignal.Context.Phase.ShouldBe(PricePhase.Close);
            exit.Context.Candle.OpenTime.ShouldBe(ParseTime(trade[4]));
            exit.Context.Phase.ShouldBe(PricePhase.Open);
            exit.Fill.Price.ShouldBe(ParseNumber(trade[5]));
            int barsHeld = exitSignal.Context.CandleIndex - entry.Context.CandleIndex + 1;
            barsHeld.ShouldBe(int.Parse(trade[6], CultureInfo.InvariantCulture));

            // The reference labels simultaneous time/target exits as time; the template emits one shared exit.
            string reason = barsHeld >= 18 ? "time" : "target";
            reason.ShouldBe(trade[7]);

            if (reason == "target")
            {
                exitSignal.Context.Candle.Close.ShouldBeGreaterThanOrEqualTo(entry.Fill.Price * 1.1m);
            }
        }
    }

    private static CompiledTradingGraph Compile(StrategyDefinition definition)
    {
        new StrategyDefinitionValidator().Validate(definition, ProductionTradingNodeTests.Catalog).Diagnostics.ShouldBeEmpty();
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(ProductionTradingNodeTests.Catalog).Lower(definition);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(lowered.Graph, ProductionTradingNodeTests.Catalog);
        compilation.Validation.Diagnostics.ShouldBeEmpty();

        return compilation.Graph.ShouldNotBeNull();
    }

    private static string[][] ReadRows(string filename)
    {
        return File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MarketData", filename))
            .Where(line => !line.StartsWith('#'))
            .Skip(1)
            .Select(line => line.Split(','))
            .ToArray();
    }

    private static DateTimeOffset ParseTime(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    }

    private static decimal ParseNumber(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}