using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Templates;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Fixtures;

public static class AtrLadderStrategyDefinitionGenerator
{
    public static StrategyDefinition Generate()
    {
        StrategyDefinition template = LadderStrategyBlockFactory.Create();
        var atr = new ValueExpression("ATR", arguments: new Dictionary<string, ValueExpression> { ["period"] = Parameter("atrPeriod") });
        ValueExpression rise = Arithmetic("Divide", Arithmetic("Multiply", Parameter("k"), Value("atr")), new ValueExpression("candle", field: "Close"));
        ValueExpression exit = Arithmetic("Multiply", Position("averageCost"), Arithmetic("Add", Constant(number: 1m, ValueType.Scalar), rise));

        return new StrategyDefinition(
            template.Parameters.Concat(
                [new StrategyParameter("atrPeriod", ValueType.Integer, DefaultValue: 2m), new StrategyParameter("k", ValueType.Scalar, DefaultValue: 0.1m)]),
            template.Values.Where(value => value.Name != "exitPrice").Concat([new StrategyValue("atr", atr), new StrategyValue("exitPrice", exit)]),
            template.Blocks,
            template.Wires,
            template.TemplateKind);
    }

    private static ValueExpression Arithmetic(string operation, ValueExpression a, ValueExpression b)
    {
        return new ValueExpression("arithmetic", operation: operation, arguments: new Dictionary<string, ValueExpression> { ["a"] = a, ["b"] = b });
    }

    private static ValueExpression Parameter(string name)
    {
        return new ValueExpression("parameter", name: name);
    }

    private static ValueExpression Value(string name)
    {
        return new ValueExpression("value", name: name);
    }

    private static ValueExpression Position(string field)
    {
        return new ValueExpression("position", field: field);
    }

    private static ValueExpression Constant(decimal number, ValueType type)
    {
        return new ValueExpression("constant", type, number);
    }
}