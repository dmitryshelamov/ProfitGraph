using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Backtesting.Execution;
using ProfitGraph.Infrastructure.Backtesting.Repositories;

namespace ProfitGraph.Infrastructure.Backtesting;

/// <summary>
///     Compacts runs whose job finished at least <see cref="BacktestExecutionOptions.CompactAfter" /> ago, including runs
///     interrupted by a restart. Runs in the Worker.
/// </summary>
internal sealed class BacktestCompactionWorker(
    IServiceScopeFactory scopes,
    IOptions<BacktestExecutionOptions> options,
    TimeProvider clock,
    ILogger<BacktestCompactionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CompactionInterval, clock);

        do
        {
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                var compactor = scope.ServiceProvider.GetRequiredService<BacktestCompactor>();
                DateTimeOffset cutoff = clock.GetUtcNow() - options.Value.CompactAfter;
                var compacted = 0;

                while (compacted < options.Value.CompactionRunsPerPass && await compactor.CompactNext(cutoff, stoppingToken))
                {
                    compacted++;
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Compacting finished backtest runs failed; the next pass retries.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}