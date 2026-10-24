using System.Globalization;
using System.Numerics;

using ProfitGraph.Engine.Graphs;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Domain.Optimization;

/// <summary>
///     An exact finite decimal grid min, min + step, …, max of one parameter. Scaled integers keep division from rounding
///     and subtraction from overflowing at decimal's limits; a grid whose values decimal cannot hold exactly is rejected.
/// </summary>
public sealed class OptimizationParameterRange
{
    private readonly BigInteger _scaledMin;
    private readonly BigInteger _scaledStep;
    private readonly int _scale;

    public PortReference Reference { get; }
    public decimal Min { get; }
    public decimal Max { get; }
    public decimal Step { get; }
    public ValueType Type { get; }
    public BigInteger Count { get; }

    public OptimizationParameterRange(PortReference reference, decimal min, decimal max, decimal step, ValueType type)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (string.IsNullOrWhiteSpace(reference.NodeId) || string.IsNullOrWhiteSpace(reference.PortKey))
        {
            throw Invalid("A range must name its parameter.");
        }

        if (step <= 0m || min > max)
        {
            throw Invalid("A range needs min <= max and a positive step.");
        }

        if (type is not (ValueType.Integer or ValueType.Scalar or ValueType.Price or ValueType.BaseAmount or ValueType.QuoteAmount))
        {
            throw Invalid("Only numeric parameters can be optimized.");
        }

        if (type == ValueType.Integer && (decimal.Truncate(min) != min || decimal.Truncate(max) != max || decimal.Truncate(step) != step))
        {
            throw Invalid("An integer parameter needs whole bounds and step.");
        }

        Reference = reference;
        Min = min;
        Max = max;
        Step = step;
        Type = type;
        _scale = Math.Max(ExactDecimalGrid.Scale(min), Math.Max(ExactDecimalGrid.Scale(max), ExactDecimalGrid.Scale(step)));
        _scaledMin = ExactDecimalGrid.ToScaledInteger(min, _scale);
        _scaledStep = ExactDecimalGrid.ToScaledInteger(step, _scale);
        Count = (ExactDecimalGrid.ToScaledInteger(max, _scale) - _scaledMin) / _scaledStep + BigInteger.One;

        if (Count > BigInteger.One)
        {
            // Adjacent values at both ends expose any precision beyond decimal's 96-bit coefficient.
            ValueAt(BigInteger.One);
            ValueAt(Count - BigInteger.One);
            ValueAt(Count - 2);
        }
    }

    public decimal ValueAt(BigInteger index)
    {
        if (index < BigInteger.Zero || index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        BigInteger value = _scaledMin + index * _scaledStep;
        decimal result = decimal.Parse(
            value.ToString(CultureInfo.InvariantCulture) + "e-" + _scale.ToString(CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);

        return ExactDecimalGrid.ToScaledInteger(result, _scale) == value
            ? result
            : throw Invalid("Every value of the range must be exactly representable as decimal.");
    }

    private static OptimizationRuleException Invalid(string message)
    {
        return new OptimizationRuleException(OptimizationRuleException.InvalidRange, message);
    }
}