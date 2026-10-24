using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Npgsql;

namespace ProfitGraph.Infrastructure.Common.Postgres;

/// <summary>
///     PostgreSQL <c>LISTEN</c> on a connection of its own for signals whose payload is an identifier; a <c>NOTIFY</c> is
///     delivered only when its transaction commits. A broken connection ends the listening with an exception; the caller
///     listens again.
/// </summary>
internal static class PostgresSignals
{
    public static async IAsyncEnumerable<Guid> Listen(
        NpgsqlDataSource dataSource,
        IReadOnlyList<string> channels,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var signals = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        connection.Notification += (_, notification) =>
        {
            if (Guid.TryParse(notification.Payload, out Guid id) && id != Guid.Empty)
            {
                signals.Writer.TryWrite(id);
            }
        };

        foreach (string channel in channels)
        {
            await using var command = new NpgsqlCommand($"LISTEN {channel};", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // The connection is not thread-safe: waiting must stop before it is disposed, or the connection stays open.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task waiting = Wait(connection, signals.Writer, stop.Token);

        try
        {
            await foreach (Guid id in signals.Reader.ReadAllAsync(cancellationToken))
            {
                yield return id;
            }
        }
        finally
        {
            await stop.CancelAsync();
            await waiting;
        }
    }

    private static async Task Wait(NpgsqlConnection connection, ChannelWriter<Guid> signals, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await connection.WaitAsync(cancellationToken);
            }
        }
        catch (Exception exception)
        {
            signals.TryComplete(exception is OperationCanceledException ? null : exception);
        }
    }
}