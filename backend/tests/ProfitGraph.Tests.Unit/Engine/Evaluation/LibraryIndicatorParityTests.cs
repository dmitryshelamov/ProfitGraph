using System.Globalization;

using NSubstitute;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Tests.Unit.Engine.Fixtures;
using ProfitGraph.Tests.Unit.Engine.Graphs;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Evaluation;

public sealed class LibraryIndicatorParityTests
{
    private static readonly PortReference IndicatorOutput = new PortReference("indicator", "out");

    private static IEnumerable<TestCaseData> RealCases()
    {
        foreach (int period in new[] { 1, 2, 3, 14, 50, 200 })
        {
            foreach (string field in new[] { "Close", "High", "High-Low" })
            {
                yield return new TestCaseData("SMA", period, field).SetName($"KASUSDT_SMA_{period}_{field}");
            }
        }

        foreach (int period in new[] { 1, 14, 50, 200 })
        {
            yield return new TestCaseData("ATR", period, "Close").SetName($"KASUSDT_ATR_{period}");
        }
    }

    [TestCaseSource(nameof(RealCases))]
    public void RealCandlesMatchDecimalOracle(string indicator, int period, string field)
    {
        Candle[] candles = LoadCandles();
        candles.Length.ShouldBe(2016);
        candles[0].OpenTime.ShouldBe(DateTimeOffset.Parse("2026-09-01T00:00:00Z", CultureInfo.InvariantCulture));

        for (var index = 1; index < candles.Length; index++)
        {
            (candles[index].OpenTime - candles[index - 1].OpenTime).ShouldBe(TimeSpan.FromMinutes(5));
        }

        Verify(indicator, period, field, candles, unavailable: false);
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(14)]
    public void UnavailableSampleMasksExactlyItsWindowAndRecovers(int period)
    {
        Candle[] candles = Enumerable.Range(start: 0, count: 80)
            .Select(index =>
            {
                decimal close = 2m + index % 7 * 0.13m + index * 0.007m;

                return new Candle(
                    DateTimeOffset.UnixEpoch.AddMinutes(index),
                    close,
                    close + 1m,
                    close - 1m,
                    close,
                    index is 0 or 20 or 21 or 37 ? 0m : 1m);
            })
            .ToArray();
        Verify("SMA", period, "Close", candles, unavailable: true);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void OutOfRangeFiniteSmaIsUnavailable(bool positive)
    {
        var buffer = new LibraryIndicatorBuffer(period: 1);
        decimal sample = positive ? decimal.MaxValue : decimal.MinValue;
        decimal? result = null;
        Should.NotThrow(() => result = buffer.Add(DateTime.UnixEpoch, sample));
        result.ShouldBeNull();
    }

    [TestCase(0.000001)]
    [TestCase(1)]
    [TestCase(1000000)]
    public void FlatGapAndPriceScalesMatchDecimalOracle(decimal scale)
    {
        Candle[] candles = Enumerable.Range(start: 0, count: 240)
            .Select(index =>
            {
                decimal close = (index < 20 ? 10m : 10m + index % 7 * 4m) * scale;
                decimal spread = index < 20 ? 0m : scale;

                return new Candle(DateTimeOffset.UnixEpoch.AddMinutes(index), close, close + spread, close - spread, close, Volume: 1m);
            })
            .ToArray();

        foreach (int period in new[] { 1, 14, 50, 200 })
        {
            Verify("SMA", period, "Close", candles, unavailable: false);
            Verify("SMA", period, "High-Low", candles, unavailable: false);
            Verify("ATR", period, "Close", candles, unavailable: false);
        }
    }

    [Test]
    public void BufferRetainsOnlyLatestResultAfterPruning()
    {
        var buffer = new LibraryIndicatorBuffer(period: 50);
        decimal? value = null;

        for (var index = 0; index < 20_000; index++)
        {
            value = buffer.Add(DateTime.UnixEpoch.AddMinutes(index), sample: 7m);
        }

        value.ShouldBe(7m);
        buffer.RetainedResultCount.ShouldBe(1);
    }

    [Test]
    public async Task SameGraphAndCandlesProduceIdenticalLedgersSequentiallyAndInParallel()
    {
        CompiledTradingGraph graph = new TradingGraphCompiler()
            .Compile(SmaStrategyGraphGenerator.Generate(), ProductionTradingNodeTests.Catalog)
            .Graph.ShouldNotBeNull();
        Candle[] candles = LoadCandles();
        var input = new BacktestInput(
            graph,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0.001m, marketFeeRate: 0.001m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.000001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("5m"),
            Array.Empty<Candle>(),
            candles);
        BacktestResult first = new BacktestEngine().Run(input);
        BacktestResult second = new BacktestEngine().Run(input);
        BacktestResult[] parallel = await Task.WhenAll(Task.Run(() => new BacktestEngine().Run(input)), Task.Run(() => new BacktestEngine().Run(input)));
        first.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        first.FillCount.ShouldBeGreaterThan(0);
        second.Ledger.ShouldBe(first.Ledger);
        parallel[0].Ledger.ShouldBe(first.Ledger);
        parallel[1].Ledger.ShouldBe(first.Ledger);
    }

    private static void Verify(string indicator, int period, string field, Candle[] candles, bool unavailable)
    {
        var nodes = new List<GraphNode> { Node("indicator", indicator, new Dictionary<string, ParamValue> { ["period"] = Number(period) }) };
        var connections = new List<GraphConnection>();
        IReadOnlyList<INodeDefinition> definitions = Definitions;

        if (indicator == "SMA")
        {
            if (unavailable)
            {
                INodeDefinition source = Source("UnavailableSample", ValueType.Price, NodeClock.CandleClocked);
                source.IsCandleClocked.Returns(true);
                source.EvaluateEngine(Arg.Any<NodeEngineEvaluationContext>())
                    .Returns(call =>
                    {
                        Candle candle = call.Arg<NodeEngineEvaluationContext>().Candle!;

                        return candle.Volume == 0m ? EngineValue.Unavailable(ValueType.Price) : EngineValue.Numeric(ValueType.Price, candle.Close);
                    });
                definitions = Definitions.Append(source).ToArray();
                nodes.Add(new GraphNode("sample", "UnavailableSample", schemaVersion: 1, new Dictionary<string, ParamValue>()));
            }
            else if (field == "High-Low")
            {
                nodes.Add(Node("high", parameters: new Dictionary<string, ParamValue> { ["field"] = Text("High") }));
                nodes.Add(Node("low", parameters: new Dictionary<string, ParamValue> { ["field"] = Text("Low") }));
                nodes.Add(Node("sample", "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = Text("Subtract") }));
                connections.Add(Wire("high", "sample", "a"));
                connections.Add(Wire("low", "sample", "b"));
            }
            else
            {
                nodes.Add(Node("sample", parameters: new Dictionary<string, ParamValue> { ["field"] = Text(field) }));
            }

            connections.Add(Wire("sample", "indicator"));
        }

        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(new StrategyGraph(nodes, connections), definitions);
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        var evaluator = new CandleValueEvaluator(compilation.Graph.ShouldNotBeNull());
        var oracle = new DecimalIndicatorOracle();
        var maxAbsolute = 0m;
        var maxRelative = 0m;
        int firstReady = -1;

        for (var index = 0; index < candles.Length; index++)
        {
            Candle candle = candles[index];
            decimal? sample = unavailable && candle.Volume == 0m
                ? null
                : field switch
                {
                    "High" => candle.High,
                    "High-Low" => candle.High - candle.Low,
                    _ => candle.Close
                };
            decimal? expected = indicator == "ATR" ? oracle.Atr(candle, period) : oracle.Sma(sample, period);
            EngineValue actual = evaluator.AdvanceClosedCandle(candle, index)[IndicatorOutput];
            actual.IsAvailable.ShouldBe(expected.HasValue, $"{indicator}/{period}/{field} candle {index}");
            evaluator.AreIndicatorsReady.ShouldBe(expected.HasValue);

            if (!expected.HasValue)
            {
                continue;
            }

            if (firstReady < 0)
            {
                firstReady = index;
            }

            decimal absolute = decimal.Abs(actual.Number - expected.Value);
            decimal relative = expected.Value == 0m ? 0m : absolute / decimal.Abs(expected.Value);
            maxAbsolute = decimal.Max(maxAbsolute, absolute);
            maxRelative = decimal.Max(maxRelative, relative);
            absolute.ShouldBeLessThanOrEqualTo(0.000000000001m * decimal.Max(x: 1m, decimal.Abs(expected.Value)), $"candle {index}");
        }

        firstReady.ShouldBe(indicator == "ATR" || unavailable ? period : period - 1);
        TestContext.Progress.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"parity={indicator}/{period}/{field}; unavailable={unavailable}; first_ready={firstReady}; max_absolute={maxAbsolute:G29}; max_relative={maxRelative:G29}"));
    }

    private static Candle[] LoadCandles()
    {
        return File.ReadLines(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "MarketData", "kasusdt-5m-20260901-20260908.csv"))
            .Where(line => !line.StartsWith('#'))
            .Skip(1)
            .Select(line =>
            {
                string[] cells = line.Split(',');

                return new Candle(
                    DateTimeOffset.Parse(cells[0], CultureInfo.InvariantCulture),
                    decimal.Parse(cells[1], CultureInfo.InvariantCulture),
                    decimal.Parse(cells[2], CultureInfo.InvariantCulture),
                    decimal.Parse(cells[3], CultureInfo.InvariantCulture),
                    decimal.Parse(cells[4], CultureInfo.InvariantCulture),
                    decimal.Parse(cells[5], CultureInfo.InvariantCulture));
            })
            .ToArray();
    }
}