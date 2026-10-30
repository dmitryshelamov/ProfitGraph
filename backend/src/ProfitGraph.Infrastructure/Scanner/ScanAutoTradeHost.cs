using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Scanner;
using ProfitGraph.Application.Scanner.AutoTrades;
using ProfitGraph.Application.Scanner.PruneScanAutoTradeRequests;

namespace ProfitGraph.Infrastructure.Scanner;

/// <summary>
///     Every <see cref="ScanAutoTradeOptions.HandleInterval" />, in the Worker's <c>live</c> role, handles up to
///     <see cref="ScanAutoTradeOptions.HandleBatchSize" /> pending auto trade requests (ADR 0071, section 6), and once an hour deletes
///     the requests past their retention. A failure is logged by kind only and ends the round; a request left claimed expires.
/// </summary>
internal sealed class ScanAutoTradeHost(
    IServiceScopeFactory scopes,
    IOptions<ScanAutoTradeOptions> options,
    TimeProvider clock,
    ILogger<ScanAutoTradeHost> logger) : BackgroundService
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.HandleInterval, clock);
        DateTimeOffset? pruned = null;

        try
        {
            do
            {
                await Round(stoppingToken);

                if (pruned is not { } last || clock.GetUtcNow() - last >= PruneInterval)
                {
                    await Send(new PruneScanAutoTradeRequestsCommand(), stoppingToken);
                    pruned = clock.GetUtcNow();
                }
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
        for (var handled = 0; handled < options.Value.HandleBatchSize; handled++)
        {
            if (!await HandleNext(stoppingToken))
            {
                return;
            }
        }
    }

    private async Task<bool> HandleNext(CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();

            return await scope.ServiceProvider.GetRequiredService<ScanAutoTradeRequestHandler>().HandleNext(stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("An auto trade request could not be handled: {ExceptionType}.", exception.GetType().Name);

            return false;
        }
    }

    private async Task Send(IRequest command, CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("The auto trade requests could not be pruned: {ExceptionType}.", exception.GetType().Name);
        }
    }
}