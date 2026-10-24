using System.Runtime.CompilerServices;

using Npgsql;

using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Backtesting;

/// <summary>
///     PostgreSQL <c>LISTEN backtest_progress</c>: the Worker notifies in the transaction that stores a run's facts or
///     stage.
/// </summary>
internal sealed class BacktestProgressNotifications(NpgsqlDataSource dataSource) : IBacktestProgressNotifications
{
    private static readonly string[] Channels = ["backtest_progress"];

    public async IAsyncEnumerable<BacktestRunId> Listen([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (Guid id in PostgresSignals.Listen(dataSource, Channels, cancellationToken))
        {
            yield return new BacktestRunId(id);
        }
    }
}