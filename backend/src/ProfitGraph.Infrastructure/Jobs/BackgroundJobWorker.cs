using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Infrastructure.Jobs.Repositories;

namespace ProfitGraph.Infrastructure.Jobs;

/// <summary>
///     The Worker's job loop: on start it fails jobs a stopped Worker left running, then it beats its heartbeat and runs
///     queued jobs one at a time on every polling interval (ADR 0011).
/// </summary>
internal sealed class BackgroundJobWorker(
    IServiceScopeFactory scopes,
    IOptions<BackgroundJobOptions> options,
    TimeProvider clock,
    ILogger<BackgroundJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterrupted(stoppingToken);
        using var timer = new PeriodicTimer(options.Value.PollingInterval, clock);

        do
        {
            await RunQueued(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RecoverInterrupted(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                int recovered = await scope.ServiceProvider.GetRequiredService<BackgroundJobRepository>()
                    .InterruptRunning(BackgroundJobRunner.RestartFailure, clock.GetUtcNow(), stoppingToken);

                if (recovered > 0)
                {
                    logger.LogWarning("Marked {Count} interrupted background jobs as failed.", recovered);
                }

                return;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Recovering interrupted jobs failed; retrying before polling.");
                await Task.Delay(options.Value.PollingInterval, clock, stoppingToken);
            }
        }
    }

    private async Task RunQueued(CancellationToken stoppingToken)
    {
        try
        {
            bool ran;

            do
            {
                using IServiceScope scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BackgroundJobRepository>().Beat(BackgroundJobOptions.WorkerName, clock.GetUtcNow(), stoppingToken);
                ran = await scope.ServiceProvider.GetRequiredService<BackgroundJobRunner>().RunNext(stoppingToken);
            }
            while (ran && !stoppingToken.IsCancellationRequested);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Running queued jobs failed; retrying on the next polling interval.");
        }
    }
}