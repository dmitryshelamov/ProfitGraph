using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.GetStrategy;

public sealed record GetStrategyQuery(Guid UserId, Guid Id) : IQuery<StrategyInternal>;