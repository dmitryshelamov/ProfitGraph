using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class GraphParamResolverTests
{
    [Test]
    public void FixedNumbersAndTextReachTypedAccessors()
    {
        ResolvedParams numeric = Resolve(new ConstantNodeDefinition(), "value", new FixedParamValue(new NumberParamLiteral(7)))!;
        ResolvedParams text = Resolve(new ArithmeticNodeDefinition(), "op", new FixedParamValue(new TextParamLiteral("Multiply")))!;

        numeric.GetNumber("value").ShouldBe(7);
        text.GetString("op").ShouldBe("Multiply");
    }

    [Test]
    public void DefaultsBecomeTypedNumbersAndText()
    {
        Resolve(new ConstantNodeDefinition())!.GetNumber("value").ShouldBe(0);
        Resolve(new ArithmeticNodeDefinition())!.GetString("op").ShouldBe("Add");
        Resolve(new SmaNodeDefinition())!.GetInt("period").ShouldBe(14);
    }

    [Test]
    public void FixedValueMustMatchDeclaredKind()
    {
        Should.Throw<ParameterResolutionException>(() =>
            Resolve(new ConstantNodeDefinition(), "value", new FixedParamValue(new TextParamLiteral("7"))));
        Should.Throw<ParameterResolutionException>(() =>
            Resolve(new ArithmeticNodeDefinition(), "op", new FixedParamValue(new NumberParamLiteral(7))));
    }

    [Test]
    public void TextMustMatchAllowedValues()
    {
        Should.Throw<ParameterResolutionException>(() =>
            Resolve(new ArithmeticNodeDefinition(), "op", new FixedParamValue(new TextParamLiteral("unsupported"))));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void FixedNonFiniteNumbersAreRejected(double value)
    {
        Should.Throw<ParameterResolutionException>(() =>
            Resolve(new ConstantNodeDefinition(), "value", new FixedParamValue(new NumberParamLiteral(value))));
    }

    [Test]
    public void OptimizeProducesNumericLiteral()
    {
        var execution = new GraphExecutionInputs(new Dictionary<PortReference, double> { [new PortReference("node", "value")] = 3 });
        ResolvedParams parameters = Resolve(new ConstantNodeDefinition(), "value", new OptimizeParamValue(Min: 1, Max: 5, Step: 2), execution: execution)!;

        parameters.GetNumber("value").ShouldBe(3);
    }

    [Test]
    public void WiredProducesNumericLiteralAndUnavailableSourceSkipsEvaluation()
    {
        var source = new PortReference("source", "out");
        var outputs = new Dictionary<PortReference, NodeOutput> { [source] = NodeOutput.Series(9) };
        var binding = new WiredParamValue(source);

        Resolve(new ConstantNodeDefinition(), "value", binding, outputs)!.GetNumber("value").ShouldBe(9);
        outputs[source] = NodeOutput.Unavailable(PortKind.Series);
        Resolve(new ConstantNodeDefinition(), "value", binding, outputs).ShouldBeNull();
    }

    [Test]
    public void WiredRequiresExistingSeriesSource()
    {
        var source = new PortReference("source", "out");
        var binding = new WiredParamValue(source);

        Should.Throw<ParameterResolutionException>(() => Resolve(new ConstantNodeDefinition(), "value", binding));
        Should.Throw<ParameterResolutionException>(() => Resolve(
            new ConstantNodeDefinition(),
            "value",
            binding,
            new Dictionary<PortReference, NodeOutput> { [source] = NodeOutput.Condition(true) }));
    }

    [Test]
    public void InterpreterAddsNodeAndBarToParameterDomainError()
    {
        var definition = new ConstantNodeDefinition();
        GraphNode node = GraphNodeGenerator.Create(
            "node",
            definition.TypeKey,
            new Dictionary<string, ParamValue> { ["value"] = new FixedParamValue(new TextParamLiteral("wrong type")) });
        var graph = new CompiledGraph([new CompiledNode(node, definition, [])]);
        Candle[] candles = [new Candle(DateTimeOffset.UnixEpoch, Open: 1m, High: 1m, Low: 1m, Close: 1m, Volume: 1m)];

        var exception = Should.Throw<GraphExecutionException>(() => new GraphInterpreter().Run(graph, candles, "BTC/USDT", "1h"));

        exception.ShouldSatisfyAllConditions(
            () => exception!.NodeId.ShouldBe("node"),
            () => exception.BarIndex.ShouldBe(0),
            () => exception.Message.ShouldContain("value", Case.Sensitive));
    }

    private static ResolvedParams? Resolve(
        INodeDefinition definition,
        string? key = null,
        ParamValue? value = null,
        IReadOnlyDictionary<PortReference, NodeOutput>? outputs = null,
        GraphExecutionInputs? execution = null)
    {
        var parameters = new Dictionary<string, ParamValue>();

        if (key is not null && value is not null)
        {
            parameters.Add(key, value);
        }

        GraphNode node = GraphNodeGenerator.Create("node", definition.TypeKey, parameters);

        return new GraphParamResolver().Resolve(
            new CompiledNode(node, definition, []),
            outputs ?? new Dictionary<PortReference, NodeOutput>(),
            execution ?? GraphExecutionInputs.Empty);
    }
}