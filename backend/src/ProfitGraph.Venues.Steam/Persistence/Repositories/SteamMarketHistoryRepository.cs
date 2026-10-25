using System.Text.Json;

using Dapper;

using Npgsql;

using ProfitGraph.Venues.Steam.History;
using ProfitGraph.Venues.Steam.Persistence.Repositories.Contracts;
using ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;

namespace ProfitGraph.Venues.Steam.Persistence.Repositories;

internal sealed class SteamMarketHistoryRepository(NpgsqlDataSource dataSource) : ISteamMarketHistoryStore
{
    public async Task<long?> Next(Guid credentialId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<long?>(
            new CommandDefinition(SteamMarketReceiptQueries.Next, new { credentialId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SteamStoredReceipt>> From(Guid credentialId, long position, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<SteamMarketReceiptDb> rows = await connection.QueryAsync<SteamMarketReceiptDb>(
            new CommandDefinition(SteamMarketReceiptQueries.From, new { credentialId, position }, cancellationToken: cancellationToken));

        return [.. rows.Select(row => new SteamStoredReceipt(row.Position, JsonSerializer.Deserialize<SteamMarketReceipt>(row.Receipt)!))];
    }

    public async Task<bool> Append(Guid credentialId, long? expectedNext, IReadOnlyList<SteamStoredReceipt> receipts, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition(SteamMarketReceiptQueries.Lock, new { key = "steam-history:" + credentialId }, transaction, cancellationToken: cancellationToken));
        var next = await connection.ExecuteScalarAsync<long?>(
            new CommandDefinition(SteamMarketReceiptQueries.Next, new { credentialId }, transaction, cancellationToken: cancellationToken));

        if (next != expectedNext)
        {
            return false;
        }

        foreach (SteamStoredReceipt stored in receipts)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    SteamMarketReceiptQueries.Add,
                    new { credentialId, stored.Position, ReceiptId = stored.Receipt.Id, Receipt = JsonSerializer.Serialize(stored.Receipt) },
                    transaction,
                    cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}