using Dapper;

using Npgsql;

using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Infrastructure.Backtesting;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;
using ProfitGraph.Infrastructure.Optimization.Repositories.Converters;
using ProfitGraph.Infrastructure.Optimization.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Optimization.Repositories;

internal sealed class OptimizationRunRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IOptimizationRunRepository
{
    public async Task Add(OptimizationRun optimization, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            Id = optimization.Id.Value,
            OwnerId = optimization.OwnerId.Value,
            StrategyId = optimization.StrategyId.Value,
            StrategyVersion = optimization.StrategyVersion.Value,
            optimization.CreatedAt,
            Configuration = BacktestJsonSerializer.Serialize(optimization.Configuration.ToDb())
        };

        await connection.ExecuteAsync(new CommandDefinition(OptimizationRunQueries.Add, parameters, Transaction(), cancellationToken: cancellationToken));
    }

    public async Task<OptimizationRun?> Get(OptimizationId id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<OptimizationRunDb>(
            new CommandDefinition(OptimizationRunQueries.Get, new { Id = id.Value }, transactions.Current, cancellationToken: cancellationToken));

        return row?.ToDomain();
    }

    public async Task Save(OptimizationRun optimization, CancellationToken cancellationToken)
    {
        BacktestHistoryInfo? history = optimization.History;
        OptimizationCounters counters = optimization.Counters;
        var parameters = new
        {
            Id = optimization.Id.Value,
            Stage = optimization.Stage.ToDb(),
            HistoryIdentity = history?.Identity,
            history?.HistoryFrom,
            ReplayFrom = history?.From,
            ReplayTo = history?.To,
            history?.WarmupCount,
            history?.ReplayCount,
            WarmupClosedIntervals = history?.WarmupClosedIntervals ?? 0,
            ReplayClosedIntervals = history?.ReplayClosedIntervals ?? 0,
            RulesSnapshotId = history?.RulesSnapshotId.Value,
            history?.BaseAsset,
            history?.QuoteAsset,
            optimization.PlannedTrials,
            counters.Completed,
            counters.RejectedByConstraint,
            counters.RejectedByTemplate,
            counters.Failed,
            BestTrialIndex = optimization.Best?.TrialIndex,
            BestObjective = optimization.Best?.Objective
        };

        if (await connection.ExecuteAsync(new CommandDefinition(OptimizationRunQueries.Save, parameters, Transaction(), cancellationToken: cancellationToken)) != 1)
        {
            throw new InvalidOperationException("The optimization to save does not exist.");
        }
    }

    private NpgsqlTransaction Transaction()
    {
        return transactions.Current ?? throw new InvalidOperationException("Optimizations are written inside a transaction.");
    }
}