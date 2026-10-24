using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Domain.Jobs;
using ProfitGraph.Infrastructure.Jobs.Repositories;

namespace ProfitGraph.Infrastructure.Jobs;

/// <summary>
///     The job loop of one Worker lane: on start it recovers the lane's jobs a stopped Worker left running, then it beats
///     its heartbeat and runs the lane's queued jobs one at a time on every polling interval (ADR 0011).
/// </summary>
internal sealed class BackgroundJobWorker(
    string lane,
    IEnumerable<BackgroundJobRegistration> registrations,
    IServiceScopeFactory scopes,
    IOptions<BackgroundJobOptions> options,
    TimeProvider clock,
    ILogger<BackgroundJobWorker> logger) : BackgroundService
{
    private readonly string[] _kinds = [.. registrations.Where(registration => registration.Lane == lane).Select(registration => registration.Kind.Value)];

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
                IReadOnlyList<BackgroundJob> interrupted = await scope.ServiceProvider.GetRequiredService<BackgroundJobRepository>()
                    .ListRunning(_kinds, stoppingToken);
                var runner = scope.ServiceProvider.GetRequiredService<BackgroundJobRunner>();

                foreach (BackgroundJob job in interrupted)
                {
                    await runner.Interrupt(job, BackgroundJobRunner.RestartFailure);
                }

                if (interrupted.Count > 0)
                {
                    logger.LogWarning("Recovered {Count} interrupted background jobs of lane {Lane}.", interrupted.Count, lane);
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
                ran = await scope.ServiceProvider.GetRequiredService<BackgroundJobRunner>().RunNext(_kinds, stoppingToken);
            }
            while (ran && !stoppingToken.IsCancellationRequested);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Running queued jobs failed; retrying on the next polling interval.");
        }
    }
}