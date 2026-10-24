using System.Runtime.CompilerServices;

using Npgsql;

using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Infrastructure.Jobs.Repositories.Queries;

namespace ProfitGraph.Infrastructure.Optimization;

/// <summary>
///     PostgreSQL <c>LISTEN</c> for optimizations: <c>optimization_progress</c> when the Worker stores a trial batch or a
///     stage, and <c>background_job_status</c> when a job's status changes, which is how an optimization pauses, resumes or
///     ends.
/// </summary>
internal sealed class OptimizationProgressNotifications(NpgsqlDataSource dataSource) : IOptimizationProgressNotifications
{
    private static readonly string[] Channels = ["optimization_progress", BackgroundJobQueries.StatusChannel];

    public async IAsyncEnumerable<OptimizationId> Listen([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (Guid id in PostgresSignals.Listen(dataSource, Channels, cancellationToken))
        {
            yield return new OptimizationId(id);
        }
    }
}