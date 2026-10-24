using Dapper;

using Npgsql;

using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Infrastructure.Backtesting;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Optimization.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Optimization.Repositories;

internal sealed class OptimizationTrialStore(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IOptimizationTrialStore
{
    public async Task Write(OptimizationId id, IReadOnlyList<OptimizationTrialInternal> trials, CancellationToken cancellationToken)
    {
        if (trials.Count == 0)
        {
            return;
        }

        string rows = BacktestJsonSerializer.Serialize(
            trials.Select(trial => new Dictionary<string, object?>
            {
                ["sequence"] = trial.Sequence,
                ["trial_index"] = trial.TrialIndex,
                ["parameters"] = trial.Parameters,
                ["status"] = trial.Status,
                ["failure_code"] = trial.FailureCode,
                ["objective"] = trial.Objective,
                ["return_percent"] = trial.ReturnPercent,
                ["total_pnl"] = trial.TotalPnl,
                ["max_drawdown_percent"] = trial.MaxDrawdownPercent,
                ["executions"] = trial.Executions,
                ["elapsed_ms"] = trial.ElapsedMs
            }));
        NpgsqlTransaction transaction = transactions.Current ?? throw new InvalidOperationException("Trials are written inside a transaction.");
        await connection.ExecuteAsync(
            new CommandDefinition(OptimizationTrialQueries.Write, new { OptimizationId = id.Value, Trials = rows }, transaction, cancellationToken: cancellationToken));
    }
}