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

    public async Task<IReadOnlyList<(Guid CredentialId, Guid BotId)>> OpenAccounts(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<(Guid CredentialId, Guid BotId)> rows = await connection.QueryAsync<(Guid CredentialId, Guid BotId)>(
            new CommandDefinition(SteamOrderJournalQueries.OpenAccounts, cancellationToken: cancellationToken));

        return [.. rows];
    }

    public async Task<bool> AddSell(SteamOrderJournalEntry entry, IReadOnlyList<SteamAssetReservation> reservations, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (await connection.ExecuteAsync(new CommandDefinition(SteamOrderJournalQueries.Add, ToDb(entry), transaction, cancellationToken: cancellationToken)) != 1)
            {
                return false;
            }

            foreach (SteamAssetReservation reservation in reservations)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(SteamAssetReservationQueries.Add, ToDb(reservation), transaction, cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);

            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<SteamAssetReservation>> Reservations(string clientOrderId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<SteamAssetReservationDb> rows = await connection.QueryAsync<SteamAssetReservationDb>(
            new CommandDefinition(SteamAssetReservationQueries.ByOrder, new { clientOrderId }, cancellationToken: cancellationToken));

        return [.. rows.Select(ToReservation)];
    }

    public async Task<IReadOnlyList<SteamAssetReservation>> Reserved(Guid credentialId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        IEnumerable<SteamAssetReservationDb> rows = await connection.QueryAsync<SteamAssetReservationDb>(
            new CommandDefinition(SteamAssetReservationQueries.OpenByCredential, new { credentialId }, cancellationToken: cancellationToken));

        return [.. rows.Select(ToReservation)];
    }

    public async Task<bool> Transition(SteamAssetReservation next, SteamReservationState expected, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        SteamAssetReservationDb row = ToDb(next);

        return await connection.ExecuteAsync(
            new CommandDefinition(
                SteamAssetReservationQueries.Transition,
                new { row.ClientOrderId, row.AppId, row.ContextId, row.AssetId, row.State, row.ListingId, row.UpdatedAt, Expected = expected.ToString() },
                cancellationToken: cancellationToken)) == 1;
    }

    private static SteamAssetReservation ToReservation(SteamAssetReservationDb row)
    {
        return new SteamAssetReservation(
            row.ClientOrderId,
            row.CredentialId,
            row.AppId,
            row.ContextId,
            row.AssetId,
            row.UnlockAt.ToUniversalTime(),
            row.SellerMinorUnits,
            Enum.Parse<SteamReservationState>(row.State),
            row.ListingId,
            row.CreatedAt.ToUniversalTime(),
            row.UpdatedAt.ToUniversalTime());
    }

    private static SteamAssetReservationDb ToDb(SteamAssetReservation reservation)
    {
        return new SteamAssetReservationDb
        {
            ClientOrderId = reservation.ClientOrderId,
            CredentialId = reservation.CredentialId,
            AppId = reservation.AppId,
            ContextId = reservation.ContextId,
            AssetId = reservation.AssetId,
            UnlockAt = reservation.UnlockAt,
            SellerMinorUnits = reservation.SellerMinorUnits,
            State = reservation.State.ToString(),
            ListingId = reservation.ListingId,
            CreatedAt = reservation.CreatedAt,
            UpdatedAt = reservation.UpdatedAt
        };
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