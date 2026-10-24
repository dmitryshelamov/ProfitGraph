using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Blocks;

public sealed record StrategyParameter(string Name, ValueType ValueType, decimal DefaultValue, StrategyParameterDisplayUnit? DisplayUnit = null);