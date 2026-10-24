using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.GetStrategyVersion;

public sealed record GetStrategyVersionQuery(Guid UserId, Guid Id, int Version) : IQuery<StrategyVersionInternal>;