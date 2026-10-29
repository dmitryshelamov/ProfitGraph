using ProfitGraph.Engine.Blocks;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Templates;

internal static class StrategyBlockExpressions
{
    internal static ValueExpression Parameter(string name)
    {
        return new ValueExpression("parameter", name: name);
    }

    internal static ValueExpression Value(string name)
    {
        return new ValueExpression("value", name: name);
    }

    internal static ValueExpression Tag(string tag)
    {
        return new ValueExpression("constant", ValueType.Tag, text: tag);
    }

    internal static ValueExpression Constant(decimal number, ValueType type)
    {
        return new ValueExpression("constant", type, number);
    }

    internal static ValueExpression Balance(string field)
    {
        return new ValueExpression("balance", field: field);
    }

    internal static ValueExpression Position(string field)
    {
        return new ValueExpression("position", field: field);
    }

    internal static ValueExpression Arithmetic(string operation, ValueExpression a, ValueExpression b)
    {
        return new ValueExpression(
            "arithmetic",
            operation: operation,
            arguments: new Dictionary<string, ValueExpression> { ["a"] = a, ["b"] = b });
    }

    internal static ValueExpression Indicator(string kind, ValueExpression input, ValueExpression period)
    {
        return new ValueExpression(kind, arguments: new Dictionary<string, ValueExpression> { ["in"] = input, ["period"] = period });
    }

    internal static ValueExpression Previous(ValueExpression input)
    {
        return new ValueExpression("previous", arguments: new Dictionary<string, ValueExpression> { ["in"] = input });
    }

    /// <summary>
    ///     The stop of a single-entry position: the last buy price lowered by the <c>stopLoss</c> fraction.
    /// </summary>
    internal static ValueExpression StopPrice()
    {
        return Arithmetic("Multiply", Position("lastBuyPrice"), Arithmetic("Subtract", Constant(number: 1m, ValueType.Scalar), Parameter("stopLoss")));
    }

    internal static StrategyCheck Cross(string type, ValueExpression a, ValueExpression b)
    {
        return new StrategyCheck(type, operands: new Dictionary<string, ValueExpression> { ["a"] = a, ["b"] = b });
    }

    internal static StrategyCheck Compare(string operation, ValueExpression a, ValueExpression b)
    {
        return new StrategyCheck(
            "Compare",
            operation,
            new Dictionary<string, ValueExpression> { ["a"] = a, ["b"] = b });
    }

    internal static StrategyCheck Preview(string side, string kind, ValueExpression quantity, ValueExpression? price = null)
    {
        var operands = new Dictionary<string, ValueExpression> { [side == "Buy" ? "amount" : "quantity"] = quantity };

        if (price is not null)
        {
            operands["price"] = price;
        }

        return new StrategyCheck("OrderSizePreviewValid", operands: operands, side: side, orderKind: kind);
    }

    internal static StrategyCheck NoTag(string tag, bool exists = false)
    {
        return new StrategyCheck("NoActiveTaggedOrder", exists ? "exists" : null, tag: Tag(tag));
    }

    internal static StrategyCheck Flat(ValueExpression quantity)
    {
        return new StrategyCheck(
            "PositionFlatPreview",
            operands:
            new Dictionary<string, ValueExpression> { ["quantity"] = quantity });
    }

    internal static StrategyWire Wire(string from, string exit, string to)
    {
        return new StrategyWire(new StrategyExit(from, exit), to);
    }
}