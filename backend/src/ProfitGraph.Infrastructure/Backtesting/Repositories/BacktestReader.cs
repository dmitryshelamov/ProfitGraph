using Dapper;

using Npgsql;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Converters;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Queries;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Backtesting.Repositories;

internal sealed class BacktestReader(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IBacktestReader
{
    public async Task<UserId?> Owner(BacktestRunId id, CancellationToken cancellationToken)
    {
        var owner = await connection.QuerySingleOrDefaultAsync<Guid?>(Command(BacktestReadQueries.Owner, new { RunId = id.Value }, cancellationToken));

        return owner is { } value ? new UserId(value) : null;
    }

    public async Task<BacktestRunStateInternal> State(BacktestRunId id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleAsync<BacktestRunStateDb>(Command(BacktestReadQueries.State, new { RunId = id.Value }, cancellationToken));

        return row.ToInternal();
    }

    public async Task<BacktestPageInternal<BacktestObservationInternal>> Observations(
        BacktestRunId id,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        BacktestCandleDb[] rows =
        [
            .. await connection.QueryAsync<BacktestCandleDb>(
                Command(BacktestReadQueries.Observations, new { RunId = id.Value, After = afterSequence, Take = limit + 1 }, cancellationToken))
        ];
        BacktestCandleDb[] page = [.. rows.Take(limit)];

        if (page.Length == 0)
        {
            return new BacktestPageInternal<BacktestObservationInternal>([], Next: null);
        }

        BacktestStoredFactDb[] ledger = await Range(
            BacktestReadQueries.LedgerRange,
            id,
            page.Min(row => row.LedgerFrom),
            page.Max(row => row.LedgerThrough),
            cancellationToken);
        BacktestStoredFactDb[] diagnostics = await Range(
            BacktestReadQueries.DiagnosticRange,
            id,
            page.Min(row => row.DiagnosticFrom),
            page.Max(row => row.DiagnosticThrough),
            cancellationToken);
        BacktestObservationInternal[] observations =
        [
            .. page.Select(row => row.ToInternal(
                [.. ledger.Where(fact => fact.Sequence > row.LedgerFrom && fact.Sequence <= row.LedgerThrough).Select(fact => fact.ToLedgerEntry())],
                [.. diagnostics.Where(fact => fact.Sequence > row.DiagnosticFrom && fact.Sequence <= row.DiagnosticThrough).Select(fact => fact.ToDiagnostic())]))
        ];

        return new BacktestPageInternal<BacktestObservationInternal>(observations, rows.Length > limit ? page[^1].Sequence : null);
    }

    public async Task<BacktestPageInternal<BacktestLedgerEntryInternal>> Orders(BacktestRunId id, long afterOrderId, int limit, CancellationToken cancellationToken)
    {
        BacktestStoredFactDb[] rows = await Page(BacktestReadQueries.Orders, id, afterOrderId, limit, cancellationToken);
        BacktestStoredFactDb[] page = [.. rows.Take(limit)];

        return new BacktestPageInternal<BacktestLedgerEntryInternal>([.. page.Select(row => row.ToLedgerEntry())], rows.Length > limit ? page[^1].OrderId : null);
    }

    public async Task<BacktestPageInternal<BacktestLedgerEntryInternal>> Fills(BacktestRunId id, long afterSequence, int limit, CancellationToken cancellationToken)
    {
        BacktestStoredFactDb[] rows = await Page(BacktestReadQueries.Fills, id, afterSequence, limit, cancellationToken);
        BacktestStoredFactDb[] page = [.. rows.Take(limit)];

        return new BacktestPageInternal<BacktestLedgerEntryInternal>([.. page.Select(row => row.ToLedgerEntry())], rows.Length > limit ? page[^1].Sequence : null);
    }

    public async Task<BacktestPageInternal<BacktestDiagnosticInternal>> Diagnostics(
        BacktestRunId id,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        BacktestStoredFactDb[] rows = await Page(BacktestReadQueries.Diagnostics, id, afterSequence, limit, cancellationToken);
        BacktestStoredFactDb[] page = [.. rows.Take(limit)];

        return new BacktestPageInternal<BacktestDiagnosticInternal>([.. page.Select(row => row.ToDiagnostic())], rows.Length > limit ? page[^1].Sequence : null);
    }

    public async Task<BacktestChartInternal> Chart(BacktestRunId id, int maxPoints, CancellationToken cancellationToken)
    {
        var parameters = new { RunId = id.Value, MaxPoints = maxPoints };
        var sizes = await connection.QuerySingleAsync<BacktestChartSizesDb>(Command(BacktestChartQueries.Sizes, parameters, cancellationToken));
        IEnumerable<BacktestChartPointDb> points = await connection.QueryAsync<BacktestChartPointDb>(
            Command(sizes.Stored is null ? BacktestChartQueries.Live : BacktestChartQueries.Stored, parameters, cancellationToken));

        return new BacktestChartInternal(sizes.Stored ?? sizes.Live, [.. points.Select(point => point.ToInternal())]);
    }

    public Task<BacktestSummaryPageInternal> ForStrategy(StrategyId strategyId, Guid? after, int limit, CancellationToken cancellationToken)
    {
        return Summaries(BacktestReadQueries.ForStrategy, new { StrategyId = strategyId.Value, After = after, Take = limit + 1 }, limit, cancellationToken);
    }

    public Task<BacktestSummaryPageInternal> Ranking(
        UserId ownerId,
        BacktestRankingFilterInternal filter,
        Guid? after,
        int limit,
        BacktestRankingSort sort,
        bool ascending,
        CancellationToken cancellationToken)
    {
        string column = sort switch
        {
            BacktestRankingSort.TotalPnl => "r.total_pnl",
            BacktestRankingSort.MaxDrawdownPercent => "r.max_drawdown_percent",
            BacktestRankingSort.CreatedAt => "r.created_at",
            _ => "r.return_percent"
        };
        var parameters = new
        {
            OwnerId = ownerId.Value,
            Status = BacktestRankingNames.Status(filter.Status),
            filter.Quote,
            filter.StrategyId,
            filter.Venue,
            filter.Symbol,
            Search = filter.Search is { } search ? $"%{EscapeLike(search)}%" : null,
            filter.Timeframe,
            Origin = filter.Origin is { } origin ? BacktestRankingNames.Origin(origin) : null,
            From = filter.From?.ToUniversalTime(),
            To = filter.To?.ToUniversalTime(),
            MinReturn = filter.MinReturnPercent,
            MaxDrawdown = filter.MaxDrawdownPercent,
            After = after,
            Take = limit + 1
        };

        return Summaries(BacktestReadQueries.Ranking(column, ascending), parameters, limit, cancellationToken);
    }

    public async Task<BacktestFacetsInternal> Facets(UserId ownerId, CancellationToken cancellationToken)
    {
        BacktestFacetDb[] rows =
            [.. await connection.QueryAsync<BacktestFacetDb>(Command(BacktestReadQueries.Facets, new { OwnerId = ownerId.Value }, cancellationToken))];

        return rows.ToInternal();
    }

    public async Task<IReadOnlyDictionary<Guid, BacktestSummaryInternal>> Latest(IReadOnlyCollection<Guid> strategyIds, CancellationToken cancellationToken)
    {
        if (strategyIds.Count == 0)
        {
            return new Dictionary<Guid, BacktestSummaryInternal>();
        }

        IEnumerable<BacktestSummaryDb> rows = await connection.QueryAsync<BacktestSummaryDb>(
            Command(BacktestReadQueries.Latest, new { StrategyIds = strategyIds.ToArray() }, cancellationToken));

        return rows.ToDictionary(row => row.StrategyId, row => row.ToInternal());
    }

    private async Task<BacktestSummaryPageInternal> Summaries(string sql, object parameters, int limit, CancellationToken cancellationToken)
    {
        BacktestSummaryDb[] rows = [.. await connection.QueryAsync<BacktestSummaryDb>(Command(sql, parameters, cancellationToken))];
        BacktestSummaryInternal[] page = [.. rows.Take(limit).Select(row => row.ToInternal())];

        return new BacktestSummaryPageInternal(page, rows.Length > limit ? page[^1].Id : null);
    }

    private async Task<BacktestStoredFactDb[]> Range(string sql, BacktestRunId id, long from, long through, CancellationToken cancellationToken)
    {
        return from >= through
            ? []
            : [.. await connection.QueryAsync<BacktestStoredFactDb>(Command(sql, new { RunId = id.Value, From = from, Through = through }, cancellationToken))];
    }

    private async Task<BacktestStoredFactDb[]> Page(string sql, BacktestRunId id, long after, int limit, CancellationToken cancellationToken)
    {
        return [.. await connection.QueryAsync<BacktestStoredFactDb>(Command(sql, new { RunId = id.Value, After = after, Take = limit + 1 }, cancellationToken))];
    }

    /// <summary>
    ///     Escapes the <c>ILIKE</c> wildcards of a typed search so that it matches as plain text.
    /// </summary>
    private static string EscapeLike(string text)
    {
        return text.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);
    }

    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}