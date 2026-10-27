using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Bots.RecheckUnresolvedOrders;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     Every five minutes, in the Worker's <c>live</c> role, checks again the bots that should run and stopped on an unresolved
///     order (ADR 0030). A bot whose venue can now prove every such order absent returns to the runtime, whose reconciliation
///     closes them; every other bot stays stopped for the owner. A failure is logged by kind only and tried again.
/// </summary>
internal sealed class UnresolvedOrderRecheckHost(IServiceScopeFactory scopes, TimeProvider clock, ILogger<UnresolvedOrderRecheckHost> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);

        try
        {
            do
            {
                await Round(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    private async Task Round(CancellationToken stoppingToken)
    {
        IReadOnlyList<BotId> stopped;

        try
        {
            using IServiceScope listing = scopes.CreateScope();
            stopped = await listing.ServiceProvider.GetRequiredService<IBotRepository>().ListDegradedOnUnresolvedOrder(stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("The bots stopped on an unresolved order could not be listed: {ExceptionType}.", exception.GetType().Name);

            return;
        }

        foreach (BotId id in stopped)
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecheckUnresolvedOrdersCommand(id.Value), stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Bot {BotId}: the unresolved order could not be checked again: {ExceptionType}.", id.Value, exception.GetType().Name);
            }
        }
    }
}
