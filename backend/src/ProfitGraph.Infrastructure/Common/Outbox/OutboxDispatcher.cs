using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ProfitGraph.Infrastructure.Common.Outbox;

/// <summary>
///     Delivers due outbox events in the order they were stored, one transaction each, and waits a polling interval when
///     none is due. Several dispatchers may run: each claims a message with <c>SKIP LOCKED</c>.
/// </summary>
internal sealed class OutboxDispatcher(IServiceScopeFactory scopes, IOptions<OutboxOptions> options, TimeProvider clock, ILogger<OutboxDispatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollingInterval, clock);

        do
        {
            await DeliverDue(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DeliverDue(CancellationToken stoppingToken)
    {
        try
        {
            bool delivered;

            do
            {
                using IServiceScope scope = scopes.CreateScope();
                delivered = await scope.ServiceProvider.GetRequiredService<OutboxDelivery>().DeliverNext(stoppingToken);
            }
            while (delivered && !stoppingToken.IsCancellationRequested);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Outbox delivery failed with {ExceptionType}; it is retried on the next pass.", exception.GetType().Name);
        }
    }
}