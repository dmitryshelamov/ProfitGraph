using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.GetStrategyVersions;

public sealed record GetStrategyVersionsQuery(Guid UserId, Guid Id) : IQuery<IReadOnlyList<StrategyVersionSummaryInternal>>;