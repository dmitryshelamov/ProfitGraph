using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class GraphTypingTests
{
    private static readonly IReadOnlyList<INodeDefinition> Definitions =
    [
        new ConstantNodeDefinition(), new ParameterNodeDefinition(), new CandleFieldNodeDefinition(), new DataSourceNodeDefinition(),
        new SmaNodeDefinition(), new PreviousCandleValueNodeDefinition(), new ArithmeticNodeDefinition(), new CompareNodeDefinition(),
        new AtrNodeDefinition(), new CrossesAboveNodeDefinition(), new IsAvailableNodeDefinition()
    ];

    [Test]
    public void PolymorphicChainInfersQuoteAndPreservesItThroughSma()
    {
        var graph = new StrategyGraph(
            [Node("price", "CandleField"), Node("quantity", type: ValueType.BaseAmount), Node("product", "Arithmetic", operation: "Multiply"), Node("mean", "SMA")],
            [Connect("price", "product", "a"), Connect("quantity", "product", "b"), Connect("product", "mean", "in")]);
        var diagnostics = new List<GraphDiagnostic>();
        IReadOnlyDictionary<PortReference, ValueType> outputs = new GraphValueTypeResolver().Resolve(graph, Definitions, diagnostics);

        diagnostics.ShouldBeEmpty();
        outputs[new PortReference("product", "out")].ShouldBe(ValueType.QuoteAmount);
        outputs[new PortReference("mean", "out")].ShouldBe(ValueType.QuoteAmount);
        new GraphValidator().Validate(graph, Definitions).IsValid.ShouldBeTrue();
    }

    [TestCase("Arithmetic")]
    [TestCase("Compare")]
    [TestCase("CrossesAbove")]
    public void NewSchemasRejectPriceAndVolumeOperands(string operationType)
    {
        var volume = new GraphNode(
            "volume",
            "CandleField",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["field"] = new FixedParamValue(new TextParamLiteral("Volume")) });
        var graph = new StrategyGraph(
            [Node("price", "CandleField"), volume, Node("operation", operationType)],
            [Connect("price", "operation", "a"), Connect("volume", "operation", "b")]);

        new GraphValidator().Validate(graph, Definitions).Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.IncompatibleUnits);
    }

    [TestCase("ATR", arg2: 1)]
    [TestCase("ATR", arg2: 2)]
    [TestCase("ATR", arg2: 4)]
    [TestCase("Constant", arg2: 0)]
    [TestCase("Constant", arg2: 1)]
    [TestCase("Constant", arg2: 3)]
    public void UnsupportedSchemasIncludingPhase1AreRejected(string key, int version)
    {
        var graph = new StrategyGraph([new GraphNode("node", key, version, new Dictionary<string, ParamValue>())], []);

        new GraphValidator().Validate(graph, Definitions).Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnsupportedSchemaVersion);
    }

    [TestCase(ValueType.Integer, arg2: true)]
    [TestCase(ValueType.Price, arg2: false)]
    [TestCase(ValueType.Scalar, arg2: false)]
    public void WiredIndicatorPeriodRequiresInteger(ValueType sourceType, bool valid)
    {
        var mean = new GraphNode(
            "mean",
            "SMA",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["period"] = new WiredParamValue(new PortReference("period", "out")) });
        var graph = new StrategyGraph([Node("price", "CandleField"), Node("period", type: sourceType), mean], [Connect("price", "mean", "in")]);

        new GraphValidator().Validate(graph, Definitions).IsValid.ShouldBe(valid);
    }

    [Test]
    public void DeclaredNonNumericTypeAndFractionalIntegerAreRejected()
    {
        new GraphValidator().Validate(new StrategyGraph([Node("tag", type: ValueType.Tag)], []), Definitions).IsValid.ShouldBeFalse();
        var integer = new GraphNode(
            "integer",
            "Constant",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new NumberParamLiteral(1.5)) },
            ValueType.Integer);
        new GraphValidator().Validate(new StrategyGraph([integer], []), Definitions).IsValid.ShouldBeFalse();
    }

    [Test]
    public void MalformedDependenciesReturnDiagnosticsWithoutInferenceExceptions()
    {
        var graph = new StrategyGraph([Node("mean", "SMA")], [Connect("missing", "mean", "in")]);
        new GraphValidator().Validate(graph, Definitions).Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.UnknownNode);
    }

    [Test]
    public void EvaluationModeIsMetadataAndDoesNotInventAnAction()
    {
        var node = new GraphNode("source", "Constant", schemaVersion: 2, new Dictionary<string, ParamValue>(), evaluationMode: ActionEvaluationMode.OnCandleClose);
        new GraphValidator().Validate(new StrategyGraph([node], []), Definitions)
            .Diagnostics.ShouldContain(diagnostic => diagnostic.Code == GraphDiagnosticCode.InvalidNodeMetadata);
    }

    [TestCase("Multiply", arg2: false)]
    [TestCase("Multiply", arg2: true)]
    [TestCase("Divide", arg2: false)]
    public void FractionalCountArithmeticCannotSupplyAnIndicatorPeriod(string operation, bool swapOperands)
    {
        var count = new GraphNode(
            "count",
            "Constant",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new NumberParamLiteral(3)) },
            ValueType.Integer);
        var scale = new GraphNode(
            "scale",
            "Constant",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new NumberParamLiteral(0.5)) });
        var mean = new GraphNode(
            "mean",
            "SMA",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["period"] = new WiredParamValue(new PortReference("arithmetic", "out")) });
        var graph = new StrategyGraph(
            [count, scale, Node("arithmetic", "Arithmetic", operation: operation), Node("price", "CandleField"), mean],
            [
                Connect(swapOperands ? "scale" : "count", "arithmetic", "a"), Connect(swapOperands ? "count" : "scale", "arithmetic", "b"),
                Connect("price", "mean", "in")
            ]);
        var diagnostics = new List<GraphDiagnostic>();
        IReadOnlyDictionary<PortReference, ValueType> outputs = new GraphValueTypeResolver().Resolve(graph, Definitions, diagnostics);

        outputs[new PortReference("arithmetic", "out")].ShouldBe(ValueType.Scalar);
        diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == GraphDiagnosticCode.IncompatibleUnits && diagnostic.NodeId == "mean" && diagnostic.ParamKey == "period");
        new GraphValidator().ValidateStrict(graph, Definitions).IsValid.ShouldBeFalse();
        new GraphCompiler().Compile(graph, Definitions).Graph.ShouldBeNull();
    }

    [TestCase("SMA", ValueType.Scalar, arg3: false)]
    [TestCase("PreviousCandleValue", ValueType.Integer, arg3: true)]
    public void AveragingCountsPromotesToScalarButPreviousSampleRemainsInteger(string nodeType, ValueType expected, bool validPeriod)
    {
        var mean = new GraphNode(
            "mean",
            "SMA",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["period"] = new WiredParamValue(new PortReference("sample", "out")) });
        var graph = new StrategyGraph(
            [Node("count", type: ValueType.Integer), Node("sample", nodeType), Node("price", "CandleField"), mean],
            [Connect("count", "sample", "in"), Connect("price", "mean", "in")]);
        var diagnostics = new List<GraphDiagnostic>();
        IReadOnlyDictionary<PortReference, ValueType> outputs = new GraphValueTypeResolver().Resolve(graph, Definitions, diagnostics);

        outputs[new PortReference("sample", "out")].ShouldBe(expected);
        new GraphValidator().Validate(graph, Definitions).IsValid.ShouldBe(validPeriod);
        (new GraphCompiler().Compile(graph, Definitions).Graph is not null).ShouldBe(validPeriod);
    }

    [TestCase(1d, 2d, 0.5d, false)]
    [TestCase(1.5d, 2d, 1d, false)]
    [TestCase(1d, 2.5d, 1d, false)]
    [TestCase(1d, 3d, 1d, true)]
    [TestCase(double.NaN, 3d, 1d, false)]
    [TestCase(1d, double.PositiveInfinity, 1d, false)]
    [TestCase(1d, 3d, 0d, false)]
    public void IntegerOptimizationBoundsAndStepMustBeIntegralAndRetainExistingConstraints(double min, double max, double step, bool valid)
    {
        var count = new GraphNode(
            "count",
            "Constant",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["value"] = new OptimizeParamValue(min, max, step) },
            ValueType.Integer);
        var mean = new GraphNode(
            "mean",
            "SMA",
            schemaVersion: 2,
            new Dictionary<string, ParamValue> { ["period"] = new WiredParamValue(new PortReference("count", "out")) });
        var graph = new StrategyGraph([count, Node("price", "CandleField"), mean], [Connect("price", "mean", "in")]);

        new GraphValidator().ValidateStrict(graph, Definitions).IsValid.ShouldBe(valid);
        (new GraphCompiler().Compile(graph, Definitions).Graph is not null).ShouldBe(valid);
    }

    private static GraphNode Node(string id, string typeKey = "Constant", ValueType? type = null, string? operation = null)
    {
        var parameters = new Dictionary<string, ParamValue>();

        if (operation is not null)
        {
            parameters["op"] = new FixedParamValue(new TextParamLiteral(operation));
        }

        if (type is ValueType.Price or ValueType.BaseAmount or ValueType.QuoteAmount)
        {
            parameters["value"] = new FixedParamValue(new DecimalParamLiteral(1m));
        }

        return new GraphNode(id, typeKey, schemaVersion: 2, parameters, type);
    }

    private static GraphConnection Connect(string source, string target, string port)
    {
        return new GraphConnection(new PortReference(source, "out"), new PortReference(target, port));
    }
}