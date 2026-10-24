using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.GetStrategyTemplates;

public sealed record GetStrategyTemplatesQuery : IQuery<IReadOnlyList<StrategyTemplateInternal>>;