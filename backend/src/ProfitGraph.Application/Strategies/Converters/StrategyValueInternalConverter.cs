using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Application.Strategies.Converters;

public static class StrategyValueInternalConverter
{
    public static StrategyValue ToDomain(this StrategyValueInternal value)
    {
        return new StrategyValue(
            value.Name,
            value.Expression.ToDomain());
    }

    public static StrategyValueInternal ToInternal(this StrategyValue value)
    {
        return new StrategyValueInternal(
            value.Name,
            value.Expression.ToInternal());
    }
}