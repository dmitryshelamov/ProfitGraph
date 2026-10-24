using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Tests.Unit.Engine.Fixtures;

public static class SmaStrategyDefinitionGenerator
{
    public static StrategyDefinition Generate(int fastPeriod = 2, int slowPeriod = 3, decimal buyAmount = 100m)
    {
        return SmaStrategyBlockFactory.Create(fastPeriod, slowPeriod, buyAmount);
    }
}