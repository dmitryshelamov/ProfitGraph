using Dapper;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Npgsql;

using ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Scanner;

/// <summary>
///     Readiness of the market scanner from its stored snapshot: healthy while none was ever written (the role does not run) or the
///     newest is fresh, Degraded once it is older than <see cref="ScannerOptions.SnapshotMaxAge" />.
/// </summary>
internal sealed class ScannerHealthCheck(NpgsqlDataSource dataSource, IOptions<ScannerOptions> options, TimeProvider clock) : IHealthCheck
{
    internal const string Name = "scanner";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var newest = await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(ScanSnapshotQueries.Newest, cancellationToken: cancellationToken));

        if (newest is null)
        {
            return HealthCheckResult.Healthy();
        }

        TimeSpan age = clock.GetUtcNow() - new DateTimeOffset(DateTime.SpecifyKind(newest.Value, DateTimeKind.Utc));

        return age <= options.Value.SnapshotMaxAge
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded($"The scanner snapshot is {age.TotalMinutes:F0} min old.");
    }
}