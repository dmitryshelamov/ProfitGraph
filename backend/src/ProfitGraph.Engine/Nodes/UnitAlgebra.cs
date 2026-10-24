namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Алгебра числовых единиц без неявных преобразований цен и сумм.
/// </summary>
public static class UnitAlgebra
{
    public static bool IsNumeric(ValueType type)
    {
        return type is ValueType.Integer or ValueType.Scalar or ValueType.Price or ValueType.BaseAmount or ValueType.QuoteAmount;
    }

    public static ValueType? Resolve(string operation, ValueType left, ValueType right)
    {
        if (!IsNumeric(left) || !IsNumeric(right))
        {
            return null;
        }

        return operation switch
        {
            "Add" or "Subtract" or "Min" or "Max" or "Modulo" when left == right => left,
            "Multiply" when (left, right) is (ValueType.Integer, ValueType.Scalar) or (ValueType.Scalar, ValueType.Integer) => ValueType.Scalar,
            "Multiply" when left == ValueType.Scalar => right,
            "Multiply" when right == ValueType.Scalar => left,
            "Multiply" when left == ValueType.Integer && right == ValueType.Integer => ValueType.Integer,
            "Multiply" when (left, right) is (ValueType.Price, ValueType.BaseAmount) or (ValueType.BaseAmount, ValueType.Price) => ValueType.QuoteAmount,
            "Divide" when left == right => ValueType.Scalar,
            "Divide" when left == ValueType.Integer && right == ValueType.Scalar => ValueType.Scalar,
            "Divide" when right == ValueType.Scalar => left,
            "Divide" when left == ValueType.QuoteAmount && right == ValueType.Price => ValueType.BaseAmount,
            "Divide" when left == ValueType.QuoteAmount && right == ValueType.BaseAmount => ValueType.Price,
            _ => null
        };
    }
}