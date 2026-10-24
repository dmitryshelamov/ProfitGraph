using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Npgsql;

using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Infrastructure.Backtesting;

/// <summary>
///     PostgreSQL <c>LISTEN backtest_progress</c> on a connection of its own; the Worker's <c>NOTIFY</c> is delivered
///     only when its transaction commits. A broken connection ends the listening with an exception; the caller listens
///     again.
/// </summary>
internal sealed class BacktestProgressNotifications(NpgsqlDataSource dataSource) : IBacktestProgressNotifications
{
    private const string Channel = "backtest_progress";

    public async IAsyncEnumerable<BacktestRunId> Listen([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var signals = System.Threading.Channels.Channel.CreateUnbounded<BacktestRunId>(new UnboundedChannelOptions { SingleReader = true });
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        connection.Notification += (_, notification) =>
        {
            if (Guid.TryParse(notification.Payload, out Guid id) && id != Guid.Empty)
            {
                signals.Writer.TryWrite(new BacktestRunId(id));
            }
        };

        await using (var command = new NpgsqlCommand($"LISTEN {Channel};", connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // The connection is not thread-safe: waiting must stop before it is disposed, or the connection stays open.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task waiting = Wait(connection, signals.Writer, stop.Token);

        try
        {
            await foreach (BacktestRunId id in signals.Reader.ReadAllAsync(cancellationToken))
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

    private static async Task Wait(NpgsqlConnection connection, ChannelWriter<BacktestRunId> signals, CancellationToken cancellationToken)
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