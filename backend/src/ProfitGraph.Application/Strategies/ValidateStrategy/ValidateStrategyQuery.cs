using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.ValidateStrategy;

public sealed record ValidateStrategyQuery(Guid UserId, Guid Id, int? Version = null, StrategyDefinitionInternal? Definition = null) : IQuery<StrategyValidationInternal>;