using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Application.Strategies.Converters;

public static class StrategyCheckInternalConverter
{
    public static StrategyCheck ToDomain(this StrategyCheckInternal value)
    {
        return new StrategyCheck(
            value.Type,
            value.Operation,
            value.Operands?.ToDictionary(pair => pair.Key, pair => pair.Value.ToDomain(), StringComparer.Ordinal),
            value.Tag?.ToDomain(),
            value.Side,
            value.OrderKind,
            value.SizeMode,
            value.MinimumOrderPolicy == "Reject" ? null : value.MinimumOrderPolicy);
    }

    public static StrategyCheckInternal ToInternal(this StrategyCheck value)
    {
        return new StrategyCheckInternal(
            value.Type,
            value.Operation,
            value.Operands?.ToDictionary(pair => pair.Key, pair => pair.Value.ToInternal(), StringComparer.Ordinal),
            value.Tag?.ToInternal(),
            value.Side,
            value.OrderKind,
            value.SizeMode,
            value.MinimumOrderPolicy == "Reject" ? null : value.MinimumOrderPolicy);
    }
}