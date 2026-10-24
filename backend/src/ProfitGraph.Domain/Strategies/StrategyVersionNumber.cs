using System.Globalization;

namespace ProfitGraph.Domain.Strategies;

/// <summary>
///     The number of a saved strategy version: starts at 1 and grows by one with every save, never reused.
/// </summary>
public readonly record struct StrategyVersionNumber
{
    public static readonly StrategyVersionNumber First = new StrategyVersionNumber(1);

    public int Value { get; }

    public StrategyVersionNumber(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, other: 1);
        Value = value;
    }

    public StrategyVersionNumber Next()
    {
        return new StrategyVersionNumber(checked(Value + 1));
    }

    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }
}