using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Tests.Unit.Engine.Fixtures;

public static class LadderStrategyDefinitionGenerator
{
    public static StrategyDefinition Generate()
    {
        return LadderStrategyBlockFactory.Create();
    }
}