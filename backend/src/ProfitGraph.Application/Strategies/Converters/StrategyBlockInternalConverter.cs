using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Application.Strategies.Converters;

public static class StrategyBlockInternalConverter
{
    public static StrategyBlock ToDomain(this StrategyBlockInternal value)
    {
        return new StrategyBlock(
            value.Id,
            value.Kind,
            value.DisplayName,
            value.Source,
            value.Join,
            value.Checks?.Select(item => item.ToDomain()).ToArray(),
            value.OrderKind,
            value.Side,
            value.Tag?.ToDomain(),
            value.Amount?.ToDomain(),
            value.Quantity?.ToDomain(),
            value.Price?.ToDomain(),
            value.SizeMode,
            value.MinimumOrderPolicy == "Reject" ? null : value.MinimumOrderPolicy,
            value.Settings?.ToDictionary(pair => pair.Key, pair => pair.Value.ToDomain(), StringComparer.Ordinal),
            value.Grid);
    }

    public static StrategyBlockInternal ToInternal(this StrategyBlock value)
    {
        return new StrategyBlockInternal(
            value.Id,
            value.Kind,
            value.DisplayName,
            value.Source,
            value.Join,
            value.Checks?.Select(item => item.ToInternal()).ToArray(),
            value.OrderKind,
            value.Side,
            value.Tag?.ToInternal(),
            value.Amount?.ToInternal(),
            value.Quantity?.ToInternal(),
            value.Price?.ToInternal(),
            value.SizeMode,
            value.MinimumOrderPolicy == "Reject" ? null : value.MinimumOrderPolicy,
            value.Settings.Count == 0 ? null : value.Settings.ToDictionary(pair => pair.Key, pair => pair.Value.ToInternal(), StringComparer.Ordinal),
            value.Grid);
    }
}