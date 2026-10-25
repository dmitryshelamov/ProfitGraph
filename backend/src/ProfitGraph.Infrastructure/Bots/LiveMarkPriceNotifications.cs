using Npgsql;

using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Infrastructure.Bots;

/// <summary>
///     PostgreSQL <c>LISTEN live_mark_price</c>: the Worker notifies in the statement that stores a newer price.
/// </summary>
internal sealed class LiveMarkPriceNotifications(NpgsqlDataSource dataSource) : ILiveMarkPriceNotifications
{
    private static readonly string[] Channels = ["live_mark_price"];

    public IAsyncEnumerable<Guid> Listen(CancellationToken cancellationToken)
    {
        return PostgresSignals.Listen(dataSource, Channels, cancellationToken);
    }
}