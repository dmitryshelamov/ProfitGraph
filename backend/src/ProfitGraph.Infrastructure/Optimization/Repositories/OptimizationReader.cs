using Dapper;

using Npgsql;

using ProfitGraph.Application.Optimization;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Optimization.Repositories.Contracts;
using ProfitGraph.Infrastructure.Optimization.Repositories.Converters;
using ProfitGraph.Infrastructure.Optimization.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Optimization.Repositories;

internal sealed class OptimizationReader(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IOptimizationReader
{
    public async Task<UserId?> Owner(OptimizationId id, CancellationToken cancellationToken)
    {
        var owner = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(OptimizationReadQueries.Owner, new { Id = id.Value }, cancellationToken));

        return owner is { } value ? new UserId(value) : null;
    }

    public async Task<OptimizationStateInternal> State(OptimizationId id, CancellationToken cancellationToken)
    {
        var status = await connection.QuerySingleAsync<string>(Command(OptimizationReadQueries.Status, new { Id = id.Value }, cancellationToken));
        Guid[] finalists = [.. await connection.QueryAsync<Guid>(Command(OptimizationReadQueries.Finalists, new { Id = id.Value }, cancellationToken))];

        return new OptimizationStateInternal(status, finalists);
    }

    public async Task<OptimizationCursorInternal> Cursor(OptimizationId id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleAsync<OptimizationCursorDb>(Command(OptimizationReadQueries.Cursor, new { Id = id.Value }, cancellationToken));

        return new OptimizationCursorInternal(row.LastSequence, row.Status);
    }

    public async Task<OptimizationTrialInternal?> Trial(OptimizationId id, int windowIndex, int trialIndex, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<OptimizationTrialDb>(
            Command(OptimizationTrialQueries.One, new { OptimizationId = id.Value, WindowIndex = windowIndex, TrialIndex = trialIndex }, cancellationToken));

        return row?.ToInternal();
    }

    public async Task<OptimizationTrialPageInternal> Trials(OptimizationId id, long afterSequence, int limit, CancellationToken cancellationToken)
    {
        OptimizationTrialDb[] rows =
        [
            .. await connection.QueryAsync<OptimizationTrialDb>(
                Command(OptimizationTrialQueries.Page, new { OptimizationId = id.Value, After = afterSequence, Take = limit + 1 }, cancellationToken))
        ];
        OptimizationTrialInternal[] page =
        [
            .. rows.Take(limit)
                .Select(row => row.ToInternal())
        ];

        return new OptimizationTrialPageInternal(page, rows.Length > limit ? page[^1].Sequence : null);
    }

    public Task<OptimizationSummaryPageInternal> ForStrategy(StrategyId strategyId, Guid? after, int limit, CancellationToken cancellationToken)
    {
        return Summaries(OptimizationReadQueries.ForStrategy, new { StrategyId = strategyId.Value, After = after, Take = limit + 1 }, limit, cancellationToken);
    }

    public Task<OptimizationSummaryPageInternal> List(
        UserId ownerId,
        OptimizationListFilterInternal filter,
        Guid? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var parameters = new
        {
            OwnerId = ownerId.Value,
            Statuses = filter.State is { } state ? Statuses(state) : null,
            filter.StrategyId,
            filter.Venue,
            Search = filter.Search is { } search ? $"%{EscapeLike(search)}%" : null,
            filter.Timeframe,
            Method = filter.Method?.ToString(),
            After = after,
            Take = limit + 1
        };

        return Summaries(OptimizationReadQueries.List, parameters, limit, cancellationToken);
    }

    /// <summary>
    ///     The job statuses of a listed state; a paused optimization is still active.
    /// </summary>
    private static string[] Statuses(OptimizationListState state)
    {
        return state switch
        {
            OptimizationListState.Active => [nameof(BackgroundJobStatus.Queued), nameof(BackgroundJobStatus.Running), nameof(BackgroundJobStatus.Paused)],
            OptimizationListState.Completed => [nameof(BackgroundJobStatus.Completed)],
            OptimizationListState.Failed => [nameof(BackgroundJobStatus.Failed)],
            OptimizationListState.Cancelled => [nameof(BackgroundJobStatus.Cancelled)],
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
    }

    /// <summary>
    ///     Escapes the <c>ILIKE</c> wildcards of a typed search so that it matches as plain text.
    /// </summary>
    private static string EscapeLike(string text)
    {
        return text.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);
    }

    private async Task<OptimizationSummaryPageInternal> Summaries(string sql, object parameters, int limit, CancellationToken cancellationToken)
    {
        OptimizationSummaryDb[] rows = [.. await connection.QueryAsync<OptimizationSummaryDb>(Command(sql, parameters, cancellationToken))];
        OptimizationSummaryInternal[] page = [.. rows.Take(limit).Select(row => row.ToInternal())];

        return new OptimizationSummaryPageInternal(page, rows.Length > limit ? page[^1].Id : null);
    }

    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}