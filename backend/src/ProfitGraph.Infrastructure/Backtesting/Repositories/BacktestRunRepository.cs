using Dapper;

using Npgsql;

using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Converters;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories;

internal sealed class BacktestRunRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBacktestRunRepository
{
    public async Task Add(BacktestRun run, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            Id = run.Id.Value,
            OwnerId = run.OwnerId.Value,
            StrategyId = run.StrategyId.Value,
            StrategyVersion = run.StrategyVersion.Value,
            run.CreatedAt,
            Configuration = BacktestJsonSerializer.Serialize(run.Configuration.ToDb())
        };

        await connection.ExecuteAsync(new CommandDefinition(BacktestRunQueries.Add, parameters, Transaction(), cancellationToken: cancellationToken));
    }

    public async Task<BacktestRun?> Get(BacktestRunId id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<BacktestRunDb>(
            new CommandDefinition(BacktestRunQueries.Get, new { Id = id.Value }, transactions.Current, cancellationToken: cancellationToken));

        return row?.ToDomain();
    }

    public async Task Save(BacktestRun run, CancellationToken cancellationToken)
    {
        BacktestHistoryInfo? history = run.History;
        BacktestRunResult? result = run.Result;
        var parameters = new
        {
            Id = run.Id.Value,
            Stage = run.Stage.ToDb(),
            HistoryIdentity = history?.Identity,
            history?.HistoryFrom,
            ReplayFrom = history?.From,
            ReplayTo = history?.To,
            history?.WarmupCount,
            history?.ReplayCount,
            RulesSnapshotId = history?.RulesSnapshotId.Value,
            history?.BaseAsset,
            history?.QuoteAsset,
            Outcome = result?.Outcome.ToDb(),
            result?.ProcessedCandles,
            result?.Equity,
            result?.TotalPnl,
            result?.FailureCode,
            result?.FailureMessage,
            Metrics = result?.Metrics is { } metrics ? BacktestJsonSerializer.Serialize(metrics) : null
        };

        if (await connection.ExecuteAsync(new CommandDefinition(BacktestRunQueries.Save, parameters, Transaction(), cancellationToken: cancellationToken)) != 1)
        {
            throw new InvalidOperationException("The backtest run to save does not exist.");
        }
    }

    private NpgsqlTransaction Transaction()
    {
        return transactions.Current ?? throw new InvalidOperationException("Backtest runs are written inside a transaction.");
    }
}