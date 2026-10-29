using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Npgsql;

namespace ProfitGraph.Infrastructure.Jobs;

/// <summary>
///     Reports Degraded when the Worker has not beaten its heartbeat for three polling intervals: jobs then wait in the
///     queue, but the API itself keeps serving.
/// </summary>
internal sealed class WorkerHeartbeatHealthCheck(NpgsqlDataSource dataSource, IOptions<BackgroundJobOptions> options, TimeProvider clock) : IHealthCheck
{
    internal const string Name = "worker";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("SELECT beat_at FROM worker_heartbeats WHERE name = $1");
        command.Parameters.AddWithValue(BackgroundJobOptions.WorkerName);
        object? beat = await command.ExecuteScalarAsync(cancellationToken);

        if (beat is not DateTime beatAt)
        {
            return HealthCheckResult.Degraded("The Worker has never run: background jobs are not processed.");
        }

        TimeSpan age = clock.GetUtcNow() - new DateTimeOffset(DateTime.SpecifyKind(beatAt, DateTimeKind.Utc));

        return IsFresh(age, options.Value)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded($"The Worker last reported {age.TotalSeconds:F0} s ago: background jobs are not processed.");
    }

    /// <summary>
    ///     A heartbeat younger than three polling intervals means the Worker is processing jobs.
    /// </summary>
    internal static bool IsFresh(TimeSpan age, BackgroundJobOptions options)
    {
        return age <= options.PollingInterval * 3;
    }
}