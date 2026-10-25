using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.SaveStrategy;

/// <summary>
///     Saves a new version. While bots of the strategy should run, the save needs <see cref="ConfirmLiveImpact" />: they
///     switch to the new version on their next root (ADR 0019).
/// </summary>
public sealed record SaveStrategyCommand(
    Guid UserId,
    Guid Id,
    int ExpectedVersion,
    string Name,
    StrategyDefinitionInternal Definition,
    GraphLayoutInternal Layout,
    bool ConfirmLiveImpact = false) : ICommand<StrategyInternal>;