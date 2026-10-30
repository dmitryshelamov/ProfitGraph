using Dapper;

using Npgsql;

using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;
using ProfitGraph.Infrastructure.Scanner.Repositories.Converters;
using ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Scanner.Repositories;

internal sealed class ScanAutoTradeRequestRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IScanAutoTradeRequestRepository
{
    public Task Add(ScanAutoTradeRequest request, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanAutoTradeRequestQueries.Add, request.ToParameters(), cancellationToken));
    }

    public async Task<IReadOnlyList<ScanAutoTradeRequest>> ClaimPending(int limit, DateTimeOffset staleBefore, CancellationToken cancellationToken)
    {
        IEnumerable<ScanAutoTradeRequestDb> rows = await connection.QueryAsync<ScanAutoTradeRequestDb>(
            Command(ScanAutoTradeRequestQueries.ClaimPending, new { Limit = limit, StaleBefore = staleBefore }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    public async Task<ScanAutoTradeRequest?> GetForUpdate(ScanAutoTradeRequestId id, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScanAutoTradeRequestDb>(
            Command(ScanAutoTradeRequestQueries.GetForUpdate, new { Id = id.Value }, cancellationToken));

        return row?.ToDomain();
    }

    public Task Update(ScanAutoTradeRequest request, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanAutoTradeRequestQueries.Update, request.ToParameters(), cancellationToken));
    }

    public Task DeleteBefore(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(Command(ScanAutoTradeRequestQueries.DeleteBefore, new { Cutoff = cutoff }, cancellationToken));
    }

    public async Task<IReadOnlyList<ScanAutoTradeRequest>> List(ScanAutoTradeRuleId ruleId, int limit, CancellationToken cancellationToken)
    {
        IEnumerable<ScanAutoTradeRequestDb> rows = await connection.QueryAsync<ScanAutoTradeRequestDb>(
            Command(ScanAutoTradeRequestQueries.ListByRule, new { RuleId = ruleId.Value, Limit = limit }, cancellationToken));

        return [.. rows.Select(row => row.ToDomain())];
    }

    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}