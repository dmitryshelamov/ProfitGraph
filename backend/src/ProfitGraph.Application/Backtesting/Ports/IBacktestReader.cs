using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Backtesting.Ports;

/// <summary>
///     Bounded reads of runs and their stored facts. Callers check ownership first with <see cref="Owner" />.
/// </summary>
public interface IBacktestReader
{
    Task<UserId?> Owner(BacktestRunId id, CancellationToken cancellationToken);

    Task<BacktestRunStateInternal> State(BacktestRunId id, CancellationToken cancellationToken);

    /// <summary>
    ///     Candle observations after <paramref name="afterSequence" /> with their facts. A finished run keeps only its last
    ///     observation; its chart and fact pages hold the rest.
    /// </summary>
    Task<BacktestPageInternal<BacktestObservationInternal>> Observations(BacktestRunId id, long afterSequence, int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     The latest state of each order, by order identifier.
    /// </summary>
    Task<BacktestPageInternal<BacktestLedgerEntryInternal>> Orders(BacktestRunId id, long afterOrderId, int limit, CancellationToken cancellationToken);

    Task<BacktestPageInternal<BacktestLedgerEntryInternal>> Fills(BacktestRunId id, long afterSequence, int limit, CancellationToken cancellationToken);

    Task<BacktestPageInternal<BacktestDiagnosticInternal>> Diagnostics(BacktestRunId id, long afterSequence, int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     The stored buckets of a compacted run, or buckets aggregated from its observations otherwise.
    /// </summary>
    Task<BacktestChartInternal> Chart(BacktestRunId id, int maxPoints, CancellationToken cancellationToken);

    Task<BacktestSummaryPageInternal> ForStrategy(StrategyId strategyId, Guid? after, int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     The owner's completed runs in one quote currency, best full-period return first.
    /// </summary>
    Task<BacktestSummaryPageInternal> Ranking(
        UserId ownerId,
        string quoteAsset,
        string? symbol,
        string? timeframe,
        Guid? after,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    ///     The newest run of each strategy that has one.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, BacktestSummaryInternal>> Latest(IReadOnlyCollection<Guid> strategyIds, CancellationToken cancellationToken);
}