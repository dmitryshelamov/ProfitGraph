using System.Globalization;

using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Graphs.Validation;

/// <summary>
///     Resolves exact typed literals; legacy double conversion is restricted to dimensionless computational values.
/// </summary>
public static class EngineLiteralResolver
{
    public static EngineValue? Resolve(ParamLiteral literal, ValueType type)
    {
        switch (literal)
        {
            case DecimalParamLiteral number when UnitAlgebra.IsNumeric(type) && (type != ValueType.Integer || decimal.Truncate(number.Value) == number.Value):
                return EngineValue.Numeric(type, number.Value);
            case NumberParamLiteral number when type is ValueType.Scalar or ValueType.Integer && double.IsFinite(number.Value):
                if (decimal.TryParse(number.Value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value)
                    && (number.Value == 0d || value != 0m) && (type != ValueType.Integer || decimal.Truncate(value) == value)
                    && double.TryParse(value.ToString("G29", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out double roundtrip)
                    && roundtrip == number.Value)
                {
                    return EngineValue.Numeric(type, value);
                }

                return null;
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
            OptimizeParamValue range => type is ValueType.Integer or ValueType.Scalar && range.Min <= range.Max && range.Step > 0d
                && Resolve(new NumberParamLiteral(range.Min), type) is not null && Resolve(new NumberParamLiteral(range.Max), type) is not null
                && Resolve(new NumberParamLiteral(range.Step), type) is not null,
            WiredParamValue => true,
            _ => false
        };
    }

    private static bool Integral(decimal value)
    {
        return decimal.Truncate(value) == value;
    }
}