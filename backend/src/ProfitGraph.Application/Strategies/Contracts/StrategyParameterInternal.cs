using ProfitGraph.Engine.Blocks;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Application.Strategies.Contracts;

public sealed record StrategyParameterInternal(string Name, ValueType ValueType, decimal DefaultValue, StrategyParameterDisplayUnit? DisplayUnit = null);