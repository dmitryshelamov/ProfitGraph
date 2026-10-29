using Dapper;

using Npgsql;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;
using ProfitGraph.Infrastructure.Scanner.Repositories.Converters;
using ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Scanner.Repositories;

internal sealed class ScanTradeSettingsRepository(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : IScanTradeSettingsRepository
{
    public async Task<ScanTradeSettings?> Get(UserId ownerId, VenueId venue, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScanTradeSettingsDb>(
            new CommandDefinition(
                ScanTradeSettingsQueries.Get,
                new { OwnerId = ownerId.Value, Venue = venue.Value },
                transactions.Current,
                cancellationToken: cancellationToken));

        return row?.ToDomain();
    }

    public Task Save(ScanTradeSettings settings, CancellationToken cancellationToken)
    {
        return connection.ExecuteAsync(new CommandDefinition(ScanTradeSettingsQueries.Save, settings.ToDb(), transactions.Current, cancellationToken: cancellationToken));
    }
}