using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.SaveStrategy;

public sealed record SaveStrategyCommand(
    Guid UserId,
    Guid Id,
    int ExpectedVersion,
    string Name,
    StrategyDefinitionInternal Definition,
    GraphLayoutInternal Layout) : ICommand<StrategyInternal>;