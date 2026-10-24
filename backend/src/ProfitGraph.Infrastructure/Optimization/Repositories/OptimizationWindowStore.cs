using Dapper;

using Npgsql;

using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Infrastructure.Backtesting;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;
using ProfitGraph.Infrastructure.Optimization.Repositories.Converters;
using ProfitGraph.Infrastructure.Optimization.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Optimization.Repositories;

internal sealed class OptimizationWindowStore(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IOptimizationWindowStore
{
    public async Task WriteWindow(OptimizationId id, WalkForwardWindowResult window, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            OptimizationId = id.Value,
            WindowIndex = window.Window.Index,
            window.Window.TrainStart,
            window.Window.TrainCount,
            window.Window.TestStart,
            window.Window.TestCount,
            window.TrainFrom,
            window.TrainTo,
            window.TestFrom,
            window.TestTo,
            window.WinnerTrialIndex,
            Parameters = window.Parameters is { } selections
                ? BacktestJsonSerializer.Serialize(selections.OptimizedValues.ToDictionary(pair => pair.Key.NodeId, pair => pair.Value))
                : null,
            TrainMetrics = window.TrainMetrics is { } train ? BacktestJsonSerializer.Serialize(train) : null,
            TestMetrics = window.TestMetrics is { } test ? BacktestJsonSerializer.Serialize(test) : null,
            Status = window.Status.ToDb(),
            window.FailureCode,
            Equity = BacktestJsonSerializer.Serialize(window.Equity)
        };

        await connection.ExecuteAsync(new CommandDefinition(OptimizationWindowQueries.Write, parameters, Transaction(), cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<WalkForwardWindowResult>> ReadWindows(OptimizationId id, CancellationToken cancellationToken)
    {
        IEnumerable<OptimizationWindowDb> rows = await connection.QueryAsync<OptimizationWindowDb>(
            new CommandDefinition(OptimizationWindowQueries.All, new { OptimizationId = id.Value }, transactions.Current, cancellationToken: cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task WriteSummary(OptimizationId id, WalkForwardSummary summary, CancellationToken cancellationToken)
    {
        var parameters = new { OptimizationId = id.Value, Summary = BacktestJsonSerializer.Serialize(summary) };
        await connection.ExecuteAsync(new CommandDefinition(OptimizationWindowQueries.WriteSummary, parameters, Transaction(), cancellationToken: cancellationToken));
    }

    public async Task<WalkForwardSummary?> ReadSummary(OptimizationId id, CancellationToken cancellationToken)
    {
        var json = await connection.QuerySingleOrDefaultAsync<string?>(
            new CommandDefinition(OptimizationWindowQueries.Summary, new { OptimizationId = id.Value }, transactions.Current, cancellationToken: cancellationToken));

        return json is null ? null : BacktestJsonSerializer.Deserialize<WalkForwardSummary>(json);
    }

    private NpgsqlTransaction Transaction()
    {
        return transactions.Current ?? throw new InvalidOperationException("Walk-forward windows are written inside a transaction.");
    }
}