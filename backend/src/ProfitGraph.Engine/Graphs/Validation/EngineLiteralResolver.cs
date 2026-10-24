using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Validation;

/// <summary>
///     Resolves exact typed literals.
/// </summary>
public static class EngineLiteralResolver
{
    public static EngineValue? Resolve(ParamLiteral literal, ValueType type)
    {
        switch (literal)
        {
            case DecimalParamLiteral number when UnitAlgebra.IsNumeric(type) && (type != ValueType.Integer || decimal.Truncate(number.Value) == number.Value):
                return EngineValue.Numeric(type, number.Value);
            case BooleanParamLiteral boolean when type == ValueType.Boolean:
                return EngineValue.FromBoolean(boolean.Value);
            case TextParamLiteral text when type == ValueType.Tag && !string.IsNullOrWhiteSpace(text.Value):
                return EngineValue.FromTag(text.Value);
            case RejectionReasonParamLiteral reason when type == ValueType.RejectionReason && Enum.IsDefined(reason.Value):
                return EngineValue.FromRejectionReason(reason.Value);
            case OrderIdParamLiteral orderId when type == ValueType.OrderId && orderId.Value > 0:
                return EngineValue.FromOrderId(orderId.Value);
            default:
                return null;
        }
    }

    public static bool IsValid(ParamValue parameter, ValueType type)
    {
        return parameter switch
        {
            FixedParamValue fixedValue => Resolve(fixedValue.Value, type) is not null,
            DecimalOptimizeParamValue range => UnitAlgebra.IsNumeric(type) && range.Min <= range.Max && range.Step > 0m
                && (type != ValueType.Integer || (Integral(range.Min) && Integral(range.Max) && Integral(range.Step))),
            WiredParamValue => true,
            _ => false
        };
    }

    private static bool Integral(decimal value)
    {
        return decimal.Truncate(value) == value;
    }
}