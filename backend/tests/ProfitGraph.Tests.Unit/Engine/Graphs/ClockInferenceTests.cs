using NSubstitute;

using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Tests.Unit.Engine.Graphs.EngineGraphFixture;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class ClockInferenceTests
{
    [Test]
    public void FixedValuesAreStaticAndWiredValuesInheritCandleClock()
    {
        var graph = new StrategyGraph(
            [
                Node("price"), Node("scale", "Constant", new Dictionary<string, ParamValue> { ["value"] = Number(2m) }),
                Node("arithmetic", "Arithmetic", new Dictionary<string, ParamValue> { ["op"] = Text("Multiply") }),
                Node(
                    "parameter",
                    "Parameter",
                    new Dictionary<string, ParamValue> { ["value"] = new WiredParamValue(new PortReference("arithmetic", "out")) },
                    ValueType.Price)
            ],
            [Wire("price", "arithmetic", "a"), Wire("scale", "arithmetic", "b")]);
        CompiledTradingGraph compiled = Compile(graph);
        compiled.Nodes.Single(node => node.Node.Id == "scale").Clock.ShouldBe(NodeClock.Static);
        compiled.Nodes.Single(node => node.Node.Id == "arithmetic").Clock.ShouldBe(NodeClock.CandleClocked);
        compiled.Nodes.Single(node => node.Node.Id == "parameter").Clock.ShouldBe(NodeClock.CandleClocked);
    }

    [TestCase("SMA", NodeClock.Static)]
    [TestCase("SMA", NodeClock.Dispatch)]
    [TestCase("SMA", NodeClock.Event)]
    [TestCase("SMA", NodeClock.Action)]
    [TestCase("PreviousCandleValue", NodeClock.Static)]
    [TestCase("PreviousCandleValue", NodeClock.Dispatch)]
    [TestCase("CrossesAbove", NodeClock.Static)]
    [TestCase("CrossesBelow", NodeClock.Dispatch)]
    public void StatefulSamplesRejectEveryNonCandleClock(string indicator, NodeClock clock)
    {
        INodeDefinition source = Source("TypedSource", ValueType.Price, clock);
        var sourceNode = new GraphNode("source", "TypedSource", schemaVersion: 1, new Dictionary<string, ParamValue>());
        GraphConnection[] connections = indicator.StartsWith("Crosses", StringComparison.Ordinal)
            ? [Wire("source", "indicator", "a"), Wire("source", "indicator", "b")]
            : [Wire("source", "indicator")];
        var graph = new StrategyGraph([sourceNode, Node("indicator", indicator)], connections);
        INodeDefinition[] definitions = Definitions.Append(source).ToArray();

        new GraphValidator().ValidateStrict(graph, definitions)
            .Diagnostics.ShouldContain(diagnostic =>
                diagnostic.Code == GraphDiagnosticCode.SmaInputNotCandleClocked && diagnostic.NodeId == "indicator");
        new TradingGraphCompiler().Compile(graph, definitions).Graph.ShouldBeNull();
    }

    [Test]
    public void PureStateCombinationRemainsDispatchClockedAndCannotFeedIndicator()
    {
        INodeDefinition source = Source("Balance", ValueType.Price, NodeClock.Dispatch);
        var graph = new StrategyGraph(
            [
                new GraphNode("state", "Balance", schemaVersion: 1, new Dictionary<string, ParamValue>()),
                Node("price"), Node("sum", "Arithmetic"), Node("mean", "SMA")
            ],
            [Wire("price", "sum", "a"), Wire("state", "sum", "b"), Wire("sum", "mean")]);
        INodeDefinition[] definitions = Definitions.Append(source).ToArray();
        new GraphValidator().ValidateStrict(graph, definitions)
            .Diagnostics.ShouldContain(diagnostic =>
                diagnostic.Code == GraphDiagnosticCode.SmaInputNotCandleClocked && diagnostic.NodeId == "mean");
    }

    [TestCase("inline")]
    [TestCase("wired")]
    [TestCase("default")]
    public void EngineOnlyInputSourcesRefusePhase1BeforeMissingInputRuntimeFailure(string mode)
    {
        INodeDefinition definition = IdentityMetadata();
        var parameters = new Dictionary<string, ParamValue>();

        if (mode == "inline")
        {
            parameters["in"] = new FixedParamValue(new NumberParamLiteral(7d));
        }
        else if (mode == "wired")
        {
            parameters["in"] = new WiredParamValue(new PortReference("number", "out"));
        }

        var identity = new GraphNode("identity", "Identity", schemaVersion: 1, parameters);
        var graph = new StrategyGraph(
            mode == "wired"
                ? [Node("number", "Constant", new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new NumberParamLiteral(7d)) }), identity]
                : [identity],
            []);
        INodeDefinition[] definitions = Definitions.Append(definition).ToArray();
        new GraphValidator().Validate(graph, definitions).IsValid.ShouldBeTrue();
        new TradingGraphCompiler().Compile(graph, definitions).Graph.ShouldNotBeNull();
        GraphCompilationResult result = new GraphCompiler().Compile(graph, definitions);
        result.Graph.ShouldBeNull();
        result.Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnsupportedPhase1Graph && diagnostic.NodeId == "identity");
    }

    [TestCase("fixed")]
    [TestCase("optimize")]
    [TestCase("wired")]
    [TestCase("default")]
    public void EquivalentRawNumericParametersKeepTheirRuntimeBindings(string mode)
    {
        INodeDefinition definition = IdentityMetadata();
        definition.Params.Returns(new[] { new ParamSpec("in", "Value", ParamValueKind.Number, DefaultValue: 7d) });
        definition.Evaluate(Arg.Any<NodeContext>(), Arg.Any<IReadOnlyDictionary<string, NodeOutput>>(), Arg.Any<ResolvedParams>())
            .Returns(call => new Dictionary<string, NodeOutput> { ["out"] = NodeOutput.Series(call.ArgAt<ResolvedParams>(2).GetNumber("in")) });
        var parameters = new Dictionary<string, ParamValue>();

        if (mode != "default")
        {
            parameters["in"] = mode switch
            {
                "fixed" => new FixedParamValue(new NumberParamLiteral(7d)),
                "optimize" => new OptimizeParamValue(Min: 1d, Max: 9d, Step: 1d),
                _ => new WiredParamValue(new PortReference("number", "out"))
            };
        }

        var identity = new GraphNode("identity", "Identity", schemaVersion: 1, parameters);
        var graph = new StrategyGraph(
            mode == "wired"
                ? [Node("number", "Constant", new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new NumberParamLiteral(7d)) }), identity]
                : [identity],
            []);
        CompiledGraph compiled = new GraphCompiler().Compile(graph, Definitions.Append(definition).ToArray()).Graph.ShouldNotBeNull();
        var choices = new GraphExecutionInputs(new Dictionary<PortReference, double> { [new PortReference("identity", "in")] = 7d });
        new GraphInterpreter().Run(compiled, [Candle(index: 0, close: 1m)], "BTCUSDT", "1h", choices)[0]
            .Outputs[new PortReference("identity", "out")]
            .SeriesValue.ShouldBe(7d);
    }

    [TestCase("missing")]
    [TestCase("default")]
    [TestCase("fixed")]
    [TestCase("optimize")]
    [TestCase("differentWired")]
    [TestCase("sameWired")]
    public void ParameterStyleConnectionRequiresAnIdenticalRawWiredBridge(string mode)
    {
        INodeDefinition definition = IdentityMetadata();
        definition.Params.Returns(new[] { new ParamSpec("in", "Value", ParamValueKind.Number, mode == "default" ? 1d : 7d) });
        definition.Evaluate(Arg.Any<NodeContext>(), Arg.Any<IReadOnlyDictionary<string, NodeOutput>>(), Arg.Any<ResolvedParams>())
            .Returns(call =>
            {
                NodeOutput connected = call.ArgAt<IReadOnlyDictionary<string, NodeOutput>>(1)["in"];
                double parameter = call.ArgAt<ResolvedParams>(2).GetNumber("in");
                connected.SeriesValue.ShouldBe(parameter);

                return new Dictionary<string, NodeOutput> { ["out"] = NodeOutput.Series(parameter) };
            });
        var parameters = new Dictionary<string, ParamValue>();

        if (mode is "sameWired" or "differentWired")
        {
            parameters["in"] = new WiredParamValue(new PortReference(mode == "sameWired" ? "number" : "other", "out"));
        }
        else if (mode == "fixed")
        {
            parameters["in"] = new FixedParamValue(new NumberParamLiteral(7d));
        }
        else if (mode == "optimize")
        {
            parameters["in"] = new OptimizeParamValue(Min: 1d, Max: 9d, Step: 1d);
        }

        var graph = new StrategyGraph(
            [
                Node("number", "Constant", new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new NumberParamLiteral(7d)) }),
                Node("other", "Constant", new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new NumberParamLiteral(9d)) }),
                new GraphNode("identity", "Identity", schemaVersion: 1, parameters)
            ],
            [Wire("number", "identity")]);
        INodeDefinition[] definitions = Definitions.Append(definition).ToArray();
        CompiledTradingGraph trading = new TradingGraphCompiler().Compile(graph, definitions).Graph.ShouldNotBeNull();
        var sources = new Dictionary<PortReference, EngineValue> { [new PortReference("number", "out")] = EngineValue.Numeric(ValueType.Scalar, value: 7m) };
        new EngineParameterResolver().Resolve(trading.Nodes.Single(node => node.Node.Id == "identity"), sources, EngineExecutionInputs.Empty)["in"].Number.ShouldBe(7m);
        GraphCompilationResult legacy = new GraphCompiler().Compile(graph, definitions);

        if (mode == "sameWired")
        {
            new GraphInterpreter().Run(legacy.Graph.ShouldNotBeNull(), [Candle(index: 0, close: 1m)], "BTCUSDT", "1h")[0]
                .Outputs[new PortReference("identity", "out")]
                .SeriesValue.ShouldBe(7d);
        }
        else
        {
            legacy.Graph.ShouldBeNull();
            legacy.Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnsupportedPhase1Graph);
        }
    }

    [TestCase(0d)]
    [TestCase(7d)]
    public void MissingOptionalEngineDefaultCannotBecomeAnAvailableRawNumber(double legacyDefault)
    {
        INodeDefinition definition = IdentityMetadata();
        definition.Params.Returns(new[] { new ParamSpec("in", "Value", ParamValueKind.Number, legacyDefault) });
        definition.EngineInputs.Returns(new[] { new EngineInputSpec("in", ValueType.Scalar, required: false, supportsInline: true) });
        var graph = new StrategyGraph([new GraphNode("identity", "Identity", schemaVersion: 1, new Dictionary<string, ParamValue>())], []);
        INodeDefinition[] definitions = Definitions.Append(definition).ToArray();
        CompiledTradingGraph trading = new TradingGraphCompiler().Compile(graph, definitions).Graph.ShouldNotBeNull();
        new EngineParameterResolver().Resolve(trading.Nodes[0], new Dictionary<PortReference, EngineValue>(), EngineExecutionInputs.Empty)["in"]
            .IsAvailable.ShouldBeFalse();
        GraphCompilationResult legacy = new GraphCompiler().Compile(graph, definitions);
        legacy.Graph.ShouldBeNull();
        legacy.Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnsupportedPhase1Graph);
    }

    [Test]
    public void EngineDefaultCannotSilentlyReplaceADifferentRawParameterDefault()
    {
        INodeDefinition definition = IdentityMetadata();
        definition.Params.Returns(new[] { new ParamSpec("in", "Value", ParamValueKind.Number, DefaultValue: 1d) });
        var graph = new StrategyGraph([new GraphNode("identity", "Identity", schemaVersion: 1, new Dictionary<string, ParamValue>())], []);
        GraphCompilationResult result = new GraphCompiler().Compile(graph, Definitions.Append(definition).ToArray());
        result.Graph.ShouldBeNull();
        result.Validation.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnsupportedPhase1Graph);
    }

    private static INodeDefinition IdentityMetadata()
    {
        var definition = Substitute.For<INodeDefinition>();
        definition.TypeKey.Returns("Identity");
        definition.ResolveClock(Arg.Any<NodeClock>()).Returns(NodeClock.Dispatch);
        definition.ResolveParameterType(Arg.Any<GraphNode>(), Arg.Any<ParamSpec>()).Returns(ValueType.Scalar);
        definition.SupportsPhase1.Returns(true);
        definition.SchemaVersion.Returns(1);
        definition.Inputs.Returns(new[] { new PortSpec("in", PortKind.Series, "Input", ValueType.Scalar) });
        definition.Outputs.Returns(new[] { new PortSpec("out", PortKind.Series, "Output", ValueType.Scalar) });
        definition.Params.Returns(Array.Empty<ParamSpec>());
        definition.EngineInputs.Returns(
            new[]
            {
                new EngineInputSpec(
                    "in",
                    ValueType.Scalar,
                    required: true,
                    supportsInline: true,
                    EngineValue.Numeric(ValueType.Scalar, value: 7m))
            });

        return definition;
    }

    [Test]
    public void HarmlessInputMetadataDoesNotRefuseExistingPhase1NumericPlan()
    {
        var definition = Substitute.For<INodeDefinition>();
        definition.TypeKey.Returns("Identity");
        definition.ResolveClock(Arg.Any<NodeClock>()).Returns(NodeClock.Dispatch);
        definition.ResolveParameterType(Arg.Any<GraphNode>(), Arg.Any<ParamSpec>()).Returns(ValueType.Scalar);
        definition.SupportsPhase1.Returns(true);
        definition.SchemaVersion.Returns(1);
        definition.Inputs.Returns(new[] { new PortSpec("in", PortKind.Series, "Input", ValueType.Scalar) });
        definition.Outputs.Returns(new[] { new PortSpec("out", PortKind.Series, "Output", ValueType.Scalar) });
        definition.Params.Returns(Array.Empty<ParamSpec>());
        definition.EngineInputs.Returns(new[] { new EngineInputSpec("in", ValueType.Scalar, required: true, supportsInline: true) });
        definition.Evaluate(Arg.Any<NodeContext>(), Arg.Any<IReadOnlyDictionary<string, NodeOutput>>(), Arg.Any<ResolvedParams>())
            .Returns(call => new Dictionary<string, NodeOutput> { ["out"] = call.ArgAt<IReadOnlyDictionary<string, NodeOutput>>(1)["in"] });
        var graph = new StrategyGraph(
            [
                Node(
                    "number",
                    "Constant",
                    new Dictionary<string, ParamValue>
                        { ["value"] = new FixedParamValue(new NumberParamLiteral(7d)) }),
                new GraphNode(
                    "identity",
                    "Identity",
                    schemaVersion: 1,
                    new Dictionary<string, ParamValue>())
            ],
            [Wire("number", "identity")]);
        CompiledGraph compiled = new GraphCompiler().Compile(graph, Definitions.Append(definition).ToArray()).Graph.ShouldNotBeNull();
        new GraphInterpreter().Run(compiled, [Candle(index: 0, close: 1m)], "BTCUSDT", "1h")[0]
            .Outputs[new PortReference("identity", "out")]
            .SeriesValue.ShouldBe(7d);
    }
}