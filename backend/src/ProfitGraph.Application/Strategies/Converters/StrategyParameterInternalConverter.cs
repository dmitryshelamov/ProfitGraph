using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Application.Strategies.Converters;

public static class StrategyParameterInternalConverter
{
    public static StrategyParameter ToDomain(this StrategyParameterInternal value)
    {
        return new StrategyParameter(
            value.Name,
            value.ValueType,
            value.DefaultValue,
            value.DisplayUnit);
    }

    public static StrategyParameterInternal ToInternal(this StrategyParameter value)
    {
        return new StrategyParameterInternal(
            value.Name,
            value.ValueType,
            value.DefaultValue,
            value.DisplayUnit);
    }
}