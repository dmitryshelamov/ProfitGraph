using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.CreateStrategy;

public sealed record CreateStrategyCommand(Guid UserId, string Name, StrategyDefinitionInternal Definition, GraphLayoutInternal Layout) : ICommand<StrategyInternal>;