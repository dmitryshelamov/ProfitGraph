using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.GetStrategies;

public sealed record GetStrategiesQuery(Guid UserId) : IQuery<IReadOnlyList<StrategySummaryInternal>>;