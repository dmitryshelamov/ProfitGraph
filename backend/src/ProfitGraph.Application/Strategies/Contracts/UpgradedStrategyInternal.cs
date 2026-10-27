namespace ProfitGraph.Application.Strategies.Contracts;

/// <summary>
///     A strategy the template upgrade saved a new version of, and how many bots that should run follow it.
/// </summary>
public sealed record UpgradedStrategyInternal(Guid StrategyId, int Version, int RunningBots);
