using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class IndicatorNodeTests
{
    [Test]
    public void EmaIsAvailableOnThePeriodthCandleAndKeepsTheInputUnit()
    {
        var graph = new StrategyGraph(
            [Node("price"), Node("ema", "EMA", Period(3m)), Node("ready", "IsAvailable")],
            [Wire("price", "ema"), Wire("ema", "ready")]);
        CompiledTradingGraph compiled = Compile(graph);
        var evaluator = new CandleValueEvaluator(compiled);

        compiled.WarmupCandles.ShouldBe(3);
        Run(evaluator, "ema", 1m, 2m).ShouldBe([null, null]);
        evaluator.AreIndicatorsReady.ShouldBeFalse();
        IReadOnlyDictionary<PortReference, EngineValue> seeded = evaluator.AdvanceClosedCandle(Candle(index: 2, close: 3m), candleIndex: 2);
        seeded[new PortReference("ema", "out")].ValueType.ShouldBe(ValueType.Price);
        seeded[new PortReference("ema", "out")].Number.ShouldBe(2m);
        seeded[new PortReference("ready", "out")].Boolean.ShouldBeTrue();
        evaluator.AreIndicatorsReady.ShouldBeTrue();
        evaluator.AdvanceClosedCandle(Candle(index: 3, close: 6m), candleIndex: 3)[new PortReference("ema", "out")].Number.ShouldBe(4m);
    }

    [Test]
    public void RsiNeedsPeriodPlusOneCandlesAndIsAScalar()
    {
        var graph = new StrategyGraph([Node("price"), Node("rsi", "RSI", Period(2m))], [Wire("price", "rsi")]);
        CompiledTradingGraph compiled = Compile(graph);

        compiled.WarmupCandles.ShouldBe(3);
        compiled.Nodes.Single(node => node.Node.Id == "rsi").OutputTypes["out"].ShouldBe(ValueType.Scalar);
        Run(new CandleValueEvaluator(compiled), "rsi", 10m, 12m, 11m, 14m).ShouldBe([null, null, 66.666666666666666666666666667m, 88.888888888888888888888888889m]);
    }

    [Test]
    public void StandardDeviationIsAvailableOnThePeriodthCandle()
    {
        var graph = new StrategyGraph([Node("price"), Node("deviation", "StdDev", Period(2m))], [Wire("price", "deviation")]);
        CompiledTradingGraph compiled = Compile(graph);

        compiled.WarmupCandles.ShouldBe(2);
        Run(new CandleValueEvaluator(compiled), "deviation", 1m, 3m, 3m).ShouldBe([null, 1m, 0m]);
    }

    [Test]
    public void BollingerBandsAreSmaPlusMinusMultiplierDeviationsAndCollapseOnAFlatWindow()
    {
        var graph = new StrategyGraph(
            [
                Node("price"),
                Node("bands", "Bollinger", new Dictionary<string, ParamValue> { ["period"] = Number(2m), ["multiplier"] = Number(1.5m) }),
                Probe("upper"), Probe("middle"), Probe("lower")
            ],
            [Wire("price", "bands"), .. Probes("bands", "upper", "middle", "lower")]);
        CompiledTradingGraph compiled = Compile(graph);
        var evaluator = new CandleValueEvaluator(compiled);

        compiled.WarmupCandles.ShouldBe(2);
        compiled.Nodes.Single(node => node.Node.Id == "upper").OutputTypes["out"].ShouldBe(ValueType.Price);
        evaluator.AdvanceClosedCandle(Candle(index: 0, close: 1m), candleIndex: 0)[new PortReference("upper", "out")].IsAvailable.ShouldBeFalse();

        // Window 1, 3: mean 2, σ 1 → 2 ± 1.5; window 3, 3: σ 0 → all bands at 3.
        Bands(evaluator.AdvanceClosedCandle(Candle(index: 1, close: 3m), candleIndex: 1)).ShouldBe((3.5m, 2m, 0.5m));
        Bands(evaluator.AdvanceClosedCandle(Candle(index: 2, close: 3m), candleIndex: 2)).ShouldBe((3m, 3m, 3m));
    }

    [Test]
    public void BollingerBandsDefaultToTwentyCandlesAndTwoDeviations()
    {
        var graph = new StrategyGraph(
            [Node("price"), Node("bands", "Bollinger"), Probe("upper"), Probe("middle"), Probe("lower")],
            [Wire("price", "bands"), .. Probes("bands", "upper", "middle", "lower")]);
        CompiledTradingGraph compiled = Compile(graph);
        var evaluator = new CandleValueEvaluator(compiled);
        IReadOnlyDictionary<PortReference, EngineValue> values = null!;

        for (var index = 0; index < 20; index++)
        {
            values = evaluator.AdvanceClosedCandle(Candle(index, index % 2 == 0 ? 9m : 11m), index);
        }

        compiled.WarmupCandles.ShouldBe(20);
        Bands(values).ShouldBe((12m, 10m, 8m));
    }

    [Test]
    public void MacdLineSignalAndHistogramFollowTheirEmas()
    {
        var graph = new StrategyGraph(
            [
                Node("price"),
                Node(
                    "indicator",
                    "MACD",
                    new Dictionary<string, ParamValue> { ["fastPeriod"] = Number(1m), ["slowPeriod"] = Number(3m), ["signalPeriod"] = Number(3m) }),
                Probe("macd"), Probe("signal"), Probe("histogram"), Node("signalReady", "IsAvailable")
            ],
            [
                Wire("price", "indicator"), .. Probes("indicator", "macd", "signal", "histogram"),
                new GraphConnection(new PortReference("indicator", "signal"), new PortReference("signalReady", "in"))
            ]);
        CompiledTradingGraph compiled = Compile(graph);
        var evaluator = new CandleValueEvaluator(compiled);
        decimal[] closes = [1m, 2m, 3m, 6m, 4m, 10m, 8m];
        var lines = new List<(decimal? Macd, decimal? Signal, decimal? Histogram, bool Ready)>();

        for (var index = 0; index < closes.Length; index++)
        {
            IReadOnlyDictionary<PortReference, EngineValue> values = evaluator.AdvanceClosedCandle(Candle(index, closes[index]), index);
            lines.Add((Read(values, "macd"), Read(values, "signal"), Read(values, "histogram"), values[new PortReference("signalReady", "out")].Boolean));
        }

        // EMA(1) is the close; EMA(3) is 2, 4, 4, 7, 7.5 from the third candle. The signal EMA(3) seeds on lines 1, 2, 0.
        compiled.WarmupCandles.ShouldBe(5);
        lines.ShouldBe(
        [
            (null, null, null, false), (null, null, null, false), (1m, null, null, false), (2m, null, null, false), (0m, 1m, -1m, true),
            (3m, 2m, 1m, true), (0.5m, 1.25m, -0.75m, true)
        ]);
    }

    [Test]
    public void MacdLineCrossesItsSignalAndHistogramComparesWithALevel()
    {
        var graph = new StrategyGraph(
            [
                Node("price"),
                Node(
                    "macd",
                    "MACD",
                    new Dictionary<string, ParamValue> { ["fastPeriod"] = Number(1m), ["slowPeriod"] = Number(3m), ["signalPeriod"] = Number(3m) }),
                Node("cross", "CrossesAbove"), Node("zero", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(0m) }, ValueType.Price),
                Node("positive", "Compare", new Dictionary<string, ParamValue> { ["op"] = Text(">") })
            ],
            [
                Wire("price", "macd"), new GraphConnection(new PortReference("macd", "macd"), new PortReference("cross", "a")),
                new GraphConnection(new PortReference("macd", "signal"), new PortReference("cross", "b")),
                new GraphConnection(new PortReference("macd", "histogram"), new PortReference("positive", "a")), Wire("zero", "positive", "b")
            ]);
        var evaluator = new CandleValueEvaluator(Compile(graph));
        decimal[] closes = [1m, 2m, 3m, 6m, 4m, 10m, 8m];
        var crosses = new List<int>();
        var positive = new List<int>();

        for (var index = 0; index < closes.Length; index++)
        {
            Candle candle = Candle(index, closes[index]);
            evaluator.AdvanceClosedCandle(candle, index);
            IReadOnlyDictionary<PortReference, EngineValue> close = evaluator.ReadDispatch(Event(candle));

            if (close[new PortReference("cross", "out")].Boolean)
            {
                crosses.Add(index);
            }

            if (close[new PortReference("positive", "out")] is { IsAvailable: true, Boolean: true })
            {
                positive.Add(index);
            }
        }

        // Histogram −1, 1, −0.75 from the fifth candle: the line crosses above its signal once.
        crosses.ShouldBe([5]);
        positive.ShouldBe([5]);
    }

    [Test]
    public void RsiComparesWithAConstantLevel()
    {
        var graph = new StrategyGraph(
            [
                Node("price"), Node("rsi", "RSI", Period(1m)),
                Node("level", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(70m) }, ValueType.Scalar),
                Node("overbought", "Compare", new Dictionary<string, ParamValue> { ["op"] = Text(">") })
            ],
            [Wire("price", "rsi"), Wire("rsi", "overbought", "a"), Wire("level", "overbought", "b")]);
        var evaluator = new CandleValueEvaluator(Compile(graph));
        var overbought = new List<bool?>();
        decimal[] closes = [1m, 2m, 1m, 1m];

        for (var index = 0; index < closes.Length; index++)
        {
            Candle candle = Candle(index, closes[index]);
            evaluator.AdvanceClosedCandle(candle, index);
            EngineValue value = evaluator.ReadDispatch(Event(candle))[new PortReference("overbought", "out")];
            overbought.Add(value.IsAvailable ? value.Boolean : null);
        }

        // RSI(1): unavailable, 100, 0, 50.
        overbought.ShouldBe([null, true, false, false]);
    }

    [Test]
    public void IndicatorPeriodsOutsideOneThroughTwoHundredAreRejected()
    {
        foreach ((string type, string key) in new[] { ("EMA", "period"), ("RSI", "period"), ("StdDev", "period"), ("Bollinger", "period"), ("MACD", "slowPeriod") })
        {
            foreach (decimal period in new[] { 0m, 201m, 2.5m })
            {
                var graph = new StrategyGraph(
                    [Node("price"), Node("indicator", type, new Dictionary<string, ParamValue> { [key] = Number(period) })],
                    [Wire("price", "indicator")]);

                TradingGraphCompilationResult result = new TradingGraphCompiler().Compile(graph, Definitions);

                result.Graph.ShouldBeNull();
                result.Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.NodeId == "indicator" && diagnostic.ParamKey == key);
            }
        }
    }

    [Test]
    public void IndicatorsRejectANonSeriesInput()
    {
        foreach (string type in new[] { "EMA", "RSI", "StdDev" })
        {
            var graph = new StrategyGraph(
                [Node("price"), Node("flag", "IsAvailable"), Node("indicator", type)],
                [Wire("price", "flag"), Wire("flag", "indicator")]);

            new TradingGraphCompiler().Compile(graph, Definitions)
                .Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.NodeId == "indicator" && diagnostic.PortKey == "in");
        }
    }

    [Test]
    public void IdenticalInputsGiveIdenticalIndicatorValuesAcrossRuns()
    {
        var graph = new StrategyGraph(
            [Node("price"), Node("rsi", "RSI", Period(5m)), Node("bands", "Bollinger", Period(7m)), Probe("upper"), Node("macd", "MACD"), Probe("histogram")],
            [Wire("price", "rsi"), Wire("price", "bands"), Wire("price", "macd"), .. Probes("bands", "upper"), .. Probes("macd", "histogram")]);
        CompiledTradingGraph compiled = Compile(graph);
        decimal[] closes = Enumerable.Range(start: 0, count: 60).Select(index => 100m + index * 37 % 11 * 0.37m - index * 0.05m).ToArray();

        Snapshot(new CandleValueEvaluator(compiled), closes).ShouldBe(Snapshot(new CandleValueEvaluator(compiled), closes));
    }

    private static Dictionary<string, ParamValue> Period(decimal period)
    {
        return new Dictionary<string, ParamValue> { ["period"] = Number(period) };
    }

    /// <summary>
    ///     Min(x, x) exposes one output port of a composite as a plain node output.
    /// </summary>
    private static GraphNode Probe(string id)
    {
        return Node(id, "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = Text("Min") });
    }

    private static IEnumerable<GraphConnection> Probes(string composite, params string[] ports)
    {
        foreach (string port in ports)
        {
            yield return new GraphConnection(new PortReference(composite, port), new PortReference(port, "a"));
            yield return new GraphConnection(new PortReference(composite, port), new PortReference(port, "b"));
        }
    }

    private static (decimal? Upper, decimal? Middle, decimal? Lower) Bands(IReadOnlyDictionary<PortReference, EngineValue> values)
    {
        return (Read(values, "upper"), Read(values, "middle"), Read(values, "lower"));
    }

    private static decimal? Read(IReadOnlyDictionary<PortReference, EngineValue> values, string node)
    {
        EngineValue value = values[new PortReference(node, "out")];

        return value.IsAvailable ? value.Number : null;
    }

    private static decimal?[] Run(CandleValueEvaluator evaluator, string node, params decimal[] closes)
    {
        return closes.Select((close, index) => Read(evaluator.AdvanceClosedCandle(Candle(index, close), index), node)).ToArray();
    }

    private static List<decimal?> Snapshot(CandleValueEvaluator evaluator, decimal[] closes)
    {
        var values = new List<decimal?>();

        for (var index = 0; index < closes.Length; index++)
        {
            IReadOnlyDictionary<PortReference, EngineValue> snapshot = evaluator.AdvanceClosedCandle(Candle(index, closes[index]), index);
            values.AddRange([Read(snapshot, "rsi"), Read(snapshot, "upper"), Read(snapshot, "histogram")]);
        }

        return values;
    }
}