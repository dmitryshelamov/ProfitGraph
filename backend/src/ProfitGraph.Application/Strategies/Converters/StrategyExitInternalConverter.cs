using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Application.Strategies.Converters;

public static class StrategyExitInternalConverter
{
    public static StrategyExit ToDomain(this StrategyExitInternal value)
    {
        return new StrategyExit(
            value.Block,
            value.Exit);
    }

    public static StrategyExitInternal ToInternal(this StrategyExit value)
    {
        return new StrategyExitInternal(
            value.Block,
            value.Exit);
    }
}