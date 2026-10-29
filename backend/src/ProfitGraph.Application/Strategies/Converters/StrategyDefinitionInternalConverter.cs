using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Application.Strategies.Converters;

public static class StrategyDefinitionInternalConverter
{
    public static StrategyDefinition ToDomain(this StrategyDefinitionInternal value)
    {
        return new StrategyDefinition(
            value.Parameters.Select(item => item.ToDomain()).ToArray(),
            value.Values.Select(item => item.ToDomain()).ToArray(),
            value.Blocks.Select(item => item.ToDomain()).ToArray(),
            value.Wires.Select(item => item.ToDomain()).ToArray(),
            value.TemplateKind,
            value.Direction);
    }

    public static StrategyDefinitionInternal ToInternal(this StrategyDefinition value)
    {
        return new StrategyDefinitionInternal(
            value.Parameters.Select(item => item.ToInternal()).ToArray(),
            value.Values.Select(item => item.ToInternal()).ToArray(),
            value.Blocks.Select(item => item.ToInternal()).ToArray(),
            value.Wires.Select(item => item.ToInternal()).ToArray(),
            value.TemplateKind,
            value.Direction);
    }
}