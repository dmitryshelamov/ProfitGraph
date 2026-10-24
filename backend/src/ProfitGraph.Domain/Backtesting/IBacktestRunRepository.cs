namespace ProfitGraph.Domain.Backtesting;

public interface IBacktestRunRepository
{
    Task Add(BacktestRun run, CancellationToken cancellationToken);

    Task<BacktestRun?> Get(BacktestRunId id, CancellationToken cancellationToken);

    /// <summary>
    ///     Stores the stage, history and result of an existing run; its configuration never changes.
    /// </summary>
    Task Save(BacktestRun run, CancellationToken cancellationToken);
}