using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class GraphInterpreterTests
{
    [Test]
    public void SmaUsesConnectedSeriesAndWarmupIsUnavailable()
    {
        var graph = new StrategyGraph(
            [
                GraphNodeGenerator.Create("source", "Constant", new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new NumberParamLiteral(5)) }),
                GraphNodeGenerator.Create("sma", "SMA", new Dictionary<string, ParamValue> { ["period"] = new FixedParamValue(new NumberParamLiteral(2)) }),
                GraphNodeGenerator.Create("ready", "IsAvailable")
            ],
            [GraphNodeGenerator.Connect("source", "sma"), GraphNodeGenerator.Connect("sma", "ready")]);
        IReadOnlyList<GraphBarResult> result = Run(graph, [10m, 20m, 30m]);
        var sma = new PortReference("sma", "out");
        var ready = new PortReference("ready", "out");

        result.ShouldSatisfyAllConditions(
            () => result[0].Outputs[sma].IsAvailable.ShouldBeFalse(),
            () => result[0].Outputs[ready].ConditionValue.ShouldBeFalse(),
            () => result[1].Outputs[sma].SeriesValue.ShouldBe(5),
            () => result[1].Outputs[ready].ConditionValue.ShouldBeTrue());
    }

    [Test]
    public void WiredParametersUseCurrentBarAndRunsDoNotShareState()
    {
        var graph = new StrategyGraph(
            [
                GraphNodeGenerator.Create("source", "DataSource"),
                new GraphNode(
                    "value",
                    "Constant",
                    schemaVersion: 2,
                    new Dictionary<string, ParamValue>
                    {
                        ["value"] = new WiredParamValue(new PortReference("source", "out"))
                    },
                    ValueType.Price)
            ],
            []);
        Run(graph, [10m, 20m]).Select(bar => bar.Outputs[new PortReference("value", "out")].SeriesValue).ShouldBe(new[] { 10d, 20d });
        Run(graph, [99m])[0].Outputs[new PortReference("value", "out")].SeriesValue.ShouldBe(99);
    }

    [Test]
    public void OptimizeRequiresExplicitValueAndDoesNotRunSweep()
    {
        var graph = new StrategyGraph(
            [
                GraphNodeGenerator.Create(
                    "value",
                    parameters: new Dictionary<string, ParamValue>
                    {
                        ["value"] = new OptimizeParamValue(Min: 1, Max: 5, Step: 2)
                    })
            ],
            []);
        var missing = Should.Throw<GraphExecutionException>(() => Run(graph, [10m]));
        missing!.NodeId.ShouldBe("value");
        var execution = new GraphExecutionInputs(new Dictionary<PortReference, double> { [new PortReference("value", "value")] = 3 });
        Run(graph, [10m], execution)[0].Outputs[new PortReference("value", "out")].SeriesValue.ShouldBe(3);
        var invalid = new GraphExecutionInputs(new Dictionary<PortReference, double> { [new PortReference("value", "value")] = 2 });
        Should.Throw<GraphExecutionException>(() => Run(graph, [10m], invalid));
    }

    [Test]
    public void CrossesAboveFiresOnlyForTheCrossingBar()
    {
        var graph = new StrategyGraph(
            [
                GraphNodeGenerator.Create("source", "DataSource"),
                GraphNodeGenerator.Create("average", "SMA", new Dictionary<string, ParamValue> { ["period"] = new FixedParamValue(new NumberParamLiteral(2)) }),
                GraphNodeGenerator.Create("cross", "CrossesAbove")
            ],
            [
                GraphNodeGenerator.Connect("source", "average"),
                GraphNodeGenerator.Connect("source", "cross", "a"),
                GraphNodeGenerator.Connect("average", "cross", "b")
            ]);

        // Средняя по двум барам: [-, 2, 1.5, 3, 2.5, 3]; цена пересекает её снизу вверх только на третьем и шестом баре.
        Run(graph, [3m, 1m, 2m, 4m, 1m, 5m])
            .Select(bar => bar.Outputs[new PortReference("cross", "out")].ConditionValue)
            .ShouldBe(new[] { false, false, true, false, false, true });
    }

    [Test]
    public void NoFutureSamplesAreReadBySma()
    {
        var graph = new StrategyGraph(
            [
                GraphNodeGenerator.Create("source", "DataSource"),
                GraphNodeGenerator.Create("sma", "SMA", new Dictionary<string, ParamValue> { ["period"] = new FixedParamValue(new NumberParamLiteral(2)) })
            ],
            [GraphNodeGenerator.Connect("source", "sma")]);
        IReadOnlyList<GraphBarResult> first = Run(graph, [1m, 3m, 999m]);
        IReadOnlyList<GraphBarResult> second = Run(graph, [1m, 3m, 7m]);
        var output = new PortReference("sma", "out");
        first[1].Outputs[output].SeriesValue.ShouldBe(second[1].Outputs[output].SeriesValue);
    }

    [Test]
    public void CancellationStopsExecution()
    {
        var graph = new StrategyGraph([GraphNodeGenerator.Create("source")], []);
        CompiledGraph compiled = new GraphCompiler().Compile(graph, GraphNodeGenerator.Definitions).Graph!;
        Should.Throw<OperationCanceledException>(() =>
            new GraphInterpreter().Run(compiled, Candles([1m]), "BTC/USDT", "1h", cancellationToken: new CancellationToken(canceled: true)));
    }

    private static IReadOnlyList<GraphBarResult> Run(StrategyGraph graph, decimal[] prices, GraphExecutionInputs? inputs = null)
    {
        GraphCompilationResult compiled = new GraphCompiler().Compile(graph, GraphNodeGenerator.Definitions);
        compiled.Validation.IsValid.ShouldBeTrue(string.Join(", ", compiled.Validation.Diagnostics.Select(diagnostic => diagnostic.Message)));

        return new GraphInterpreter().Run(compiled.Graph!, Candles(prices), "BTC/USDT", "1h", inputs);
    }

    private static Candle[] Candles(decimal[] prices)
    {
        return prices.Select((price, index) => new Candle(DateTimeOffset.UnixEpoch.AddHours(index), price, price, price, price, Volume: 1m)).ToArray();
    }
}