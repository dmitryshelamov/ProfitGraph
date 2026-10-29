using Dapper;

using Npgsql;

using ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Scanner;

/// <summary>
///     The one-scanner-at-a-time lock (ADR 0059): a PostgreSQL session advisory lock on a connection of its own, released when
///     the connection closes, so a second Worker with the <c>scanner</c> role waits instead of doubling the venue connections.
/// </summary>
internal sealed class ScannerLock(NpgsqlDataSource dataSource)
{
    /// <summary>
    ///     The connection that holds the lock, or null when another process holds it.
    /// </summary>
    public async Task<NpgsqlConnection?> TryAcquire(CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition(ScannerLockQueries.TryAcquire, cancellationToken: cancellationToken)))
            {
                return connection;
            }
        }
        catch
        {
            await connection.DisposeAsync();

            throw;
        }

        await connection.DisposeAsync();

        return null;
    }

    /// <summary>
    ///     Returns when the lock's connection fails a check; the caller then stops scanning.
    /// </summary>
    public static async Task Hold(NpgsqlConnection connection, TimeSpan interval, TimeProvider clock, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(interval, clock, cancellationToken);
                await connection.ExecuteScalarAsync<int>(new CommandDefinition(ScannerLockQueries.Check, cancellationToken: cancellationToken));
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The connection is gone, and with it the lock.
        }
    }
}