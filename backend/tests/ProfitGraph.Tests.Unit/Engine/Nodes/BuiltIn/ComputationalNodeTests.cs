using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;

namespace ProfitGraph.Tests.Unit.Engine.Nodes.BuiltIn;

public sealed class ComputationalNodeTests
{
    [TestCase("Add", arg2: 6d)]
    [TestCase("Subtract", arg2: 2d)]
    [TestCase("Multiply", arg2: 8d)]
    [TestCase("Divide", arg2: 2d)]
    [TestCase("Min", arg2: 2d)]
    [TestCase("Max", arg2: 4d)]
    [TestCase("Modulo", arg2: 0d)]
    public void ArithmeticEvaluatesSelectedOperation(string op, double expected)
    {
        var inputs = new Dictionary<string, NodeOutput> { ["a"] = NodeOutput.Series(4), ["b"] = NodeOutput.Series(2) };
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral> { ["op"] = new TextParamLiteral(op) });
        NodeOutput output = new ArithmeticNodeDefinition().Evaluate(Context(), inputs, parameters)["out"];
        output.SeriesValue.ShouldBe(expected);
    }

    [TestCase("<", arg2: false)]
    [TestCase("<=", arg2: false)]
    [TestCase("=", arg2: false)]
    [TestCase("!=", arg2: true)]
    [TestCase(">=", arg2: true)]
    [TestCase(">", arg2: true)]
    public void CompareEvaluatesSelectedOperation(string op, bool expected)
    {
        var inputs = new Dictionary<string, NodeOutput> { ["a"] = NodeOutput.Series(4), ["b"] = NodeOutput.Series(2) };
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral> { ["op"] = new TextParamLiteral(op) });
        NodeOutput output = new CompareNodeDefinition().Evaluate(Context(), inputs, parameters)["out"];
        output.ConditionValue.ShouldBe(expected);
    }

    [TestCase("Open", arg2: 10d)]
    [TestCase("High", arg2: 12d)]
    [TestCase("Low", arg2: 9d)]
    [TestCase("Close", arg2: 11d)]
    [TestCase("Volume", arg2: 100d)]
    public void CandleFieldReadsOnlySelectedField(string field, double expected)
    {
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral> { ["field"] = new TextParamLiteral(field) });
        NodeOutput output = new CandleFieldNodeDefinition().Evaluate(Context(), new Dictionary<string, NodeOutput>(), parameters)["out"];
        output.SeriesValue.ShouldBe(expected);
    }

    [TestCase(arg1: false, arg2: false)]
    [TestCase(arg1: false, arg2: true)]
    [TestCase(arg1: true, arg2: false)]
    [TestCase(arg1: true, arg2: true)]
    public void BooleanNodesPreserveTruthTables(bool a, bool b)
    {
        var inputs = new Dictionary<string, NodeOutput>
        {
            ["a"] = NodeOutput.Condition(a),
            ["b"] = NodeOutput.Condition(b),
            ["in"] = NodeOutput.Condition(a)
        };
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral>());
        parameters.ShouldSatisfyAllConditions(
            () => new AndNodeDefinition().Evaluate(Context(), inputs, parameters)["out"].ConditionValue.ShouldBe(a && b),
            () => new OrNodeDefinition().Evaluate(Context(), inputs, parameters)["out"].ConditionValue.ShouldBe(a || b),
            () => new NotNodeDefinition().Evaluate(Context(), inputs, parameters)["out"].ConditionValue.ShouldBe(!a));
    }

    [TestCase(arg1: false, arg2: false)]
    [TestCase(arg1: false, arg2: true)]
    [TestCase(arg1: true, arg2: false)]
    [TestCase(arg1: true, arg2: true)]
    public void EventFlagsAreMergedAndFilteredWithoutTradingSideEffects(bool eventFired, bool condition)
    {
        var inputs = new Dictionary<string, NodeOutput>
        {
            ["a"] = NodeOutput.Event(eventFired),
            ["b"] = NodeOutput.Event(condition),
            ["event"] = NodeOutput.Event(eventFired),
            ["condition"] = NodeOutput.Condition(condition)
        };
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral>());
        parameters.ShouldSatisfyAllConditions(
            () => new MergeEventsNodeDefinition().Evaluate(Context(), inputs, parameters)["out"].EventFired.ShouldBe(eventFired || condition),
            () => new FilterEventNodeDefinition().Evaluate(Context(), inputs, parameters)["out"].EventFired.ShouldBe(eventFired && condition),
            () => new OnStartNodeDefinition().Evaluate(Context(), inputs, parameters)["out"].EventFired.ShouldBeTrue(),
            () => new OnCandleClosedNodeDefinition().Evaluate(Context(), inputs, parameters)["out"].EventFired.ShouldBeTrue());
    }

    [Test]
    public void DivisionByZeroDoesNotProduceNumericOutput()
    {
        var inputs = new Dictionary<string, NodeOutput> { ["a"] = NodeOutput.Series(4), ["b"] = NodeOutput.Series(0) };
        var parameters = new ResolvedParams(new Dictionary<string, ParamLiteral> { ["op"] = new TextParamLiteral("Divide") });
        Should.Throw<InvalidOperationException>(() => new ArithmeticNodeDefinition().Evaluate(Context(), inputs, parameters));
    }

    private static NodeContext Context()
    {
        Candle[] candles = [new Candle(DateTimeOffset.UnixEpoch, Open: 10m, High: 12m, Low: 9m, Close: 11m, Volume: 100m)];

        return new NodeContext(barIndex: 0, candles, "BTC/USDT", "1h");
    }
}