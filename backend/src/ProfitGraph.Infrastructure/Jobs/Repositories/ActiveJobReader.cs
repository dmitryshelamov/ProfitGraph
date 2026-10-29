using Dapper;

using Microsoft.Extensions.Options;

using Npgsql;

using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Jobs.Repositories.Contracts;
using ProfitGraph.Infrastructure.Jobs.Repositories.Converters;
using ProfitGraph.Infrastructure.Jobs.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Jobs.Repositories;

/// <summary>
///     Reads the owner's active jobs with their lanes from the registered kinds, so a queued job's place matches the order
///     the Worker claims jobs in.
/// </summary>
internal sealed class ActiveJobReader(
    NpgsqlConnection connection,
    PostgresTransactionAccessor transactions,
    IEnumerable<BackgroundJobRegistration> registrations,
    IOptions<BackgroundJobOptions> options,
    TimeProvider clock) : IActiveJobReader
{
    private readonly BackgroundJobRegistration[] _registrations = [.. registrations];

    public async Task<ActiveJobsInternal> ForOwner(UserId ownerId, CancellationToken cancellationToken)
    {
        var parameters = new
        {
            OwnerId = ownerId.Value,
            Kinds = _registrations.Select(registration => registration.Kind.Value).ToArray(),
            Lanes = _registrations.Select(registration => registration.Lane).ToArray()
        };
        ActiveJobDb[] rows = [.. await connection.QueryAsync<ActiveJobDb>(Command(ActiveJobQueries.ForOwner, parameters, cancellationToken))];
        var beat = await connection.QuerySingleOrDefaultAsync<DateTime?>(
            Command(ActiveJobQueries.Heartbeat, new { Name = BackgroundJobOptions.WorkerName }, cancellationToken));
        bool online = beat is { } beatAt
            && WorkerHeartbeatHealthCheck.IsFresh(clock.GetUtcNow() - new DateTimeOffset(DateTime.SpecifyKind(beatAt, DateTimeKind.Utc)), options.Value);

        return new ActiveJobsInternal([.. rows.Select(row => row.ToInternal())], online);
    }

    private CommandDefinition Command(string sql, object parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(sql, parameters, transactions.Current, cancellationToken: cancellationToken);
    }
}