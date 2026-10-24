using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Npgsql;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     Reports Degraded when bots should run but no Worker with the live role has beaten its heartbeat for three polling
///     intervals: those bots are then not executed. A fresh heartbeat proves the runtime loop, not venue connectivity.
/// </summary>
internal sealed class LiveRuntimeHealthCheck(NpgsqlDataSource dataSource, IOptions<BotRuntimeOptions> options, TimeProvider clock) : IHealthCheck
{
    internal const string Name = "live";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            SELECT EXISTS (SELECT 1 FROM bots WHERE desired_state = 'Running'),
                (SELECT beat_at FROM worker_heartbeats WHERE name = $1)
            """);
        command.Parameters.AddWithValue(BotRuntimeOptions.HeartbeatName);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        if (!reader.GetBoolean(0))
        {
            return HealthCheckResult.Healthy();
        }

        if (reader.IsDBNull(1))
        {
            return HealthCheckResult.Degraded("Bots should run, but no live Worker has ever run.");
        }

        TimeSpan age = clock.GetUtcNow() - new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));

        return age <= options.Value.PollingInterval * 3
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded($"Bots should run, but the live Worker last reported {age.TotalSeconds:F0} s ago.");
    }
}