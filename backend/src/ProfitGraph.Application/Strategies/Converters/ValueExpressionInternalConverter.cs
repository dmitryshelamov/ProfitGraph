using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Application.Strategies.Converters;

public static class ValueExpressionInternalConverter
{
    public static ValueExpression ToDomain(this ValueExpressionInternal value)
    {
        return new ValueExpression(
            value.Kind,
            value.ValueType,
            value.Number,
            value.Text,
            value.Name,
            value.Field,
            value.Operation,
            value.Arguments?.ToDictionary(pair => pair.Key, pair => pair.Value.ToDomain(), StringComparer.Ordinal));
    }

    public static ValueExpressionInternal ToInternal(this ValueExpression value)
    {
        return new ValueExpressionInternal(
            value.Kind,
            value.ValueType,
            value.Number,
            value.Text,
            value.Name,
            value.Field,
            value.Operation,
            value.Arguments?.ToDictionary(pair => pair.Key, pair => pair.Value.ToInternal(), StringComparer.Ordinal));
    }
}