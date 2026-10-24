using Microsoft.Extensions.Diagnostics.HealthChecks;

using Npgsql;

namespace ProfitGraph.Infrastructure.Common.Postgres;

/// <summary>
///     Readiness check: the database accepts connections and answers a trivial query.
/// </summary>
internal sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    internal const string Name = "postgres";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using NpgsqlCommand command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        }
    }
}