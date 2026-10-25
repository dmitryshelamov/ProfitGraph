using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.GetStrategyLiveImpact;

/// <summary>
///     The bots of the owner's strategy that should run, which a save reaches (ADR 0019).
/// </summary>
public sealed record GetStrategyLiveImpactQuery(Guid UserId, Guid Id) : IQuery<IReadOnlyList<LiveImpactBotInternal>>;