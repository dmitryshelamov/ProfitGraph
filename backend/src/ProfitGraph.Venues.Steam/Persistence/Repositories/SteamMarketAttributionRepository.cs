using Dapper;

using Npgsql;

using ProfitGraph.Venues.Steam.History;
using ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;

namespace ProfitGraph.Venues.Steam.Persistence.Repositories;

internal sealed class SteamMarketAttributionRepository(NpgsqlDataSource dataSource) : ISteamMarketAttributions
{
    public async Task<IReadOnlyList<SteamAttribution>> Of(Guid credentialId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<SteamAttribution> rows = await connection.QueryAsync<SteamAttribution>(
            new CommandDefinition(SteamMarketAttributionQueries.Of, new { credentialId }, cancellationToken: cancellationToken));

        return [.. rows];
    }

    public async Task<bool> Attribute(Guid credentialId, IReadOnlyList<SteamAttribution> attributions, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (SteamAttribution attribution in attributions)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        SteamMarketAttributionQueries.Add,
                        new { credentialId, attribution.ReceiptId, attribution.ClientOrderId, attribution.BotId, at },
                        transaction,
                        cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);

            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }
}