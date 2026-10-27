using Microsoft.Extensions.Diagnostics.HealthChecks;

using Npgsql;

namespace ProfitGraph.Infrastructure.Common.Outbox;

/// <summary>
///     Reports Degraded while parked outbox messages exist (ADR 0045): their effects, such as alerts and feed entries, are
///     missing until an operator fixes the cause and releases them. The API itself keeps serving.
/// </summary>
internal sealed class OutboxHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    internal const string Name = "outbox";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(OutboxQueries.CountParked);
        var parked = (long)(await command.ExecuteScalarAsync(cancellationToken))!;

        return parked == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded($"{parked} outbox message(s) are parked after repeated delivery failures: their effects are missing.");
    }
}
