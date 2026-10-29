using Npgsql;

using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Scanner;

/// <summary>
///     PostgreSQL <c>LISTEN scan_snapshot</c>: the scanner notifies in the transaction that replaces a snapshot.
/// </summary>
internal sealed class ScanSnapshotNotifications(NpgsqlDataSource dataSource) : IScanSnapshotNotifications
{
    private static readonly string[] Channels = ["scan_snapshot"];

    public IAsyncEnumerable<Guid> Listen(CancellationToken cancellationToken)
    {
        return PostgresSignals.Listen(dataSource, Channels, cancellationToken);
    }
}