using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Common.Outbox.PruneOutbox;

namespace ProfitGraph.Infrastructure.Common.Outbox;

/// <summary>
///     Deletes messages delivered longer than <see cref="OutboxOptions.Retention" /> ago, once an hour, in bounded batches of
///     one transaction each, in the Worker's <c>live</c> role (ADR 0045). Undelivered and parked messages stay.
/// </summary>
internal sealed class OutboxRetentionHost(IServiceScopeFactory scopes, IOptions<OutboxOptions> options, TimeProvider clock, ILogger<OutboxRetentionHost> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);

        try
        {
            do
            {
                try
                {
                    await Prune(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Pruning the outbox failed with {ExceptionType}; it is tried again.", exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    private async Task Prune(CancellationToken stoppingToken)
    {
        var command = new PruneOutboxCommand(options.Value.Retention, options.Value.RetentionBatchSize);
        int removed;

        do
        {
            using IServiceScope scope = scopes.CreateScope();
            removed = await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, stoppingToken);
        }
        while (removed == command.BatchSize && !stoppingToken.IsCancellationRequested);
    }
}