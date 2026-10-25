using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Bots.PruneBotFeed;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     Removes bot feed entries older than 90 days, once an hour, in the Worker's <c>live</c> role (ADR 0021).
/// </summary>
internal sealed class BotFeedRetentionHost(IServiceScopeFactory scopes, TimeProvider clock, ILogger<BotFeedRetentionHost> logger) : BackgroundService
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
                    using IServiceScope scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ISender>().Send(new PruneBotFeedCommand(), stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Pruning the bot feed failed with {ExceptionType}; it is tried again.", exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }
}