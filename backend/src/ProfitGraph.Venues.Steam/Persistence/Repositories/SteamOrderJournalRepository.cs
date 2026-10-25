using Dapper;

using Npgsql;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Venues.Steam.Orders;
using ProfitGraph.Venues.Steam.Persistence.Repositories.Contracts;
using ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;

namespace ProfitGraph.Venues.Steam.Persistence.Repositories;

internal sealed class SteamOrderJournalRepository(NpgsqlDataSource dataSource) : ISteamOrderJournal
{
    public async Task<SteamOrderJournalEntry?> Get(string clientOrderId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SteamOrderJournalDb>(
            new CommandDefinition(SteamOrderJournalQueries.Get, new { clientOrderId }, cancellationToken: cancellationToken));

        return row is null ? null : ToEntry(row);
    }

    public async Task<bool> Add(SteamOrderJournalEntry entry, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            return await connection.ExecuteAsync(new CommandDefinition(SteamOrderJournalQueries.Add, ToDb(entry), cancellationToken: cancellationToken)) == 1;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    public async Task<bool> Transition(SteamOrderJournalEntry next, SteamOrderJournalState expected, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        SteamOrderJournalDb row = ToDb(next);

        try
        {
            return await connection.ExecuteAsync(
                new CommandDefinition(
                    SteamOrderJournalQueries.Transition,
                    new
                    {
                        row.ClientOrderId,
                        row.State,
                        row.WaitingCode,
                        row.SteamOrderId,
                        row.ConfirmationCreator,
                        row.Outcome,
                        row.Quantity,
                        row.PriceMinorUnits,
                        row.PlaceStartedAt,
                        row.Deadline,
                        row.UpdatedAt,
                        Expected = expected.ToString()
                    },
                    cancellationToken: cancellationToken)) == 1;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<SteamOrderJournalEntry>> Open(Guid credentialId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<SteamOrderJournalDb> rows = await connection.QueryAsync<SteamOrderJournalDb>(
            new CommandDefinition(SteamOrderJournalQueries.Open, new { credentialId }, cancellationToken: cancellationToken));

        return [.. rows.Select(ToEntry)];
    }

    private static SteamOrderJournalEntry ToEntry(SteamOrderJournalDb row)
    {
        return new SteamOrderJournalEntry(
            row.ClientOrderId,
            row.BotId,
            row.CredentialId,
            row.EngineOrderId,
            row.Instrument,
            Enum.Parse<OrderSide>(row.Side),
            Enum.Parse<OrderType>(row.OrderType),
            row.Quantity,
            row.PriceMinorUnits,
            Enum.Parse<SteamOrderJournalState>(row.State),
            row.WaitingCode is null ? null : Enum.Parse<SteamWaitingCode>(row.WaitingCode),
            row.SteamOrderId,
            row.ConfirmationCreator,
            row.Outcome is null ? null : Enum.Parse<SteamOrderOutcome>(row.Outcome),
            row.PlaceStartedAt?.ToUniversalTime(),
            row.Deadline?.ToUniversalTime(),
            row.CreatedAt.ToUniversalTime(),
            row.UpdatedAt.ToUniversalTime());
    }

    private static SteamOrderJournalDb ToDb(SteamOrderJournalEntry entry)
    {
        return new SteamOrderJournalDb
        {
            ClientOrderId = entry.ClientOrderId,
            BotId = entry.BotId,
            CredentialId = entry.CredentialId,
            EngineOrderId = entry.EngineOrderId,
            Instrument = entry.Instrument,
            Side = entry.Side.ToString(),
            OrderType = entry.Type.ToString(),
            Quantity = entry.Quantity,
            PriceMinorUnits = entry.PriceMinorUnits,
            State = entry.State.ToString(),
            WaitingCode = entry.WaitingCode?.ToString(),
            SteamOrderId = entry.SteamOrderId,
            ConfirmationCreator = entry.ConfirmationCreator,
            Outcome = entry.Outcome?.ToString(),
            PlaceStartedAt = entry.PlaceStartedAt,
            Deadline = entry.Deadline,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt
        };
    }
}