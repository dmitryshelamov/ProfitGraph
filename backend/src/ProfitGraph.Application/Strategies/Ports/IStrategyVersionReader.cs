using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.Ports;

/// <summary>
///     Lists version summaries without loading their definitions.
/// </summary>
public interface IStrategyVersionReader
{
    Task<IReadOnlyList<StrategyVersionSummaryInternal>> List(Guid strategyId, CancellationToken cancellationToken);
}