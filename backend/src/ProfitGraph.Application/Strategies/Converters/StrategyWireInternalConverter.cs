using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Application.Strategies.Converters;

public static class StrategyWireInternalConverter
{
    public static StrategyWire ToDomain(this StrategyWireInternal value)
    {
        return new StrategyWire(
            value.From.ToDomain(),
            value.To);
    }

    public static StrategyWireInternal ToInternal(this StrategyWire value)
    {
        return new StrategyWireInternal(
            value.From.ToInternal(),
            value.To);
    }
}