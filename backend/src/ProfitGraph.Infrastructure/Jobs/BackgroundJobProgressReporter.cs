using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Infrastructure.Jobs.Repositories;

namespace ProfitGraph.Infrastructure.Jobs;

/// <summary>
///     Writes progress of the job attached to the current DI scope; without one it does nothing. Each update commits on
///     its own, outside the work's transactions.
/// </summary>
internal sealed class BackgroundJobProgressReporter(BackgroundJobRepository repository, TimeProvider clock, ILogger<BackgroundJobProgressReporter> logger)
    : IBackgroundJobProgress
{
    private BackgroundJob? _job;

    public async Task Report(long completed, long total, CancellationToken cancellationToken)
    {
        if (_job is null)
        {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();
        _job.ReportProgress(completed, total, now);

        if (!await repository.ReportProgress(_job.Id, completed, total, now, cancellationToken))
        {
            logger.LogWarning("Job progress was not saved because the job is no longer running.");
        }
    }

    internal void Attach(BackgroundJob job)
    {
        _job = job;
    }
}