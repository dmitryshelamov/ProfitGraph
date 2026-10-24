using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Decimal-арифметика и сравнения нод Arithmetic и Compare. Деление на ноль бросает исключение и останавливает прогон.
/// </summary>
internal static class EngineOperators
{
    public static EngineValue Arithmetic(ValueType type, decimal a, decimal b, string operation)
    {
        decimal result = operation switch
        {
            "Add" => a + b,
            "Subtract" => a - b,
            "Multiply" => a * b,
            "Divide" => a / b,
            "Min" => decimal.Min(a, b),
            "Max" => decimal.Max(a, b),
            "Modulo" => a % b,
            _ => throw new InvalidOperationException("Unknown arithmetic operation.")
        };

        return EngineValue.Numeric(type, result);
    }

    /// <summary>
    ///     Числа сравниваются всеми операторами; enum-значения — только на равенство и неравенство.
    /// </summary>
    public static bool Compare(EngineValue a, EngineValue b, string operation)
    {
        if (UnitAlgebra.IsNumeric(a.ValueType))
        {
            return operation switch
            {
                "<" => a.Number < b.Number,
                "<=" => a.Number <= b.Number,
                "=" => a.Number == b.Number,
                "!=" => a.Number != b.Number,
                ">=" => a.Number >= b.Number,
                ">" => a.Number > b.Number,
                _ => throw new InvalidOperationException("Unknown comparison operation.")
            };
        }

        bool equal = a.ValueType switch
        {
            ValueType.OrderSide => a.Side == b.Side,
            ValueType.OrderType => a.Type == b.Type,
            ValueType.RejectionReason => a.Reason == b.Reason,
            _ => throw new InvalidOperationException("Unsupported concrete comparison type.")
        };

        return operation switch
        {
            "=" => equal,
            "!=" => !equal,
            _ => throw new InvalidOperationException("Enums support equality and inequality only.")
        };
    }
}