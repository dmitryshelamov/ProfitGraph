using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Scanner;
using ProfitGraph.Application.Scanner.TrackScanTrade;
using ProfitGraph.Domain.Scanner.Trades;

namespace ProfitGraph.Infrastructure.Scanner;

/// <summary>
///     Every <see cref="ScanTradeOptions.TrackInterval" />, in the Worker's <c>live</c> role, looks at each live scanner trade and each
///     finished one whose bot was not released (ADR 0060): overdue entries are cancelled, entries that ended without buying end their
///     trade, and finished trades stop their bots. A failure is logged by kind only and tried again in the next round.
/// </summary>
internal sealed class ScanTradeTrackingHost(
    IServiceScopeFactory scopes,
    IOptions<ScanTradeOptions> options,
    TimeProvider clock,
    ILogger<ScanTradeTrackingHost> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.TrackInterval, clock);

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
        List<ScanTradeId> ids;

        try
        {
            using IServiceScope listing = scopes.CreateScope();
            var trades = listing.ServiceProvider.GetRequiredService<IScanTradeRepository>();
            int limit = options.Value.TrackBatchSize;
            ids = [.. (await trades.ListLive(limit, stoppingToken)).Concat(await trades.ListUnreleased(limit, stoppingToken)).Select(trade => trade.Id)];
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("The scanner trades could not be listed: {ExceptionType}.", exception.GetType().Name);

            return;
        }

        foreach (ScanTradeId id in ids)
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TrackScanTradeCommand(id.Value), stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Scanner trade {TradeId} could not be tracked: {ExceptionType}.", id.Value, exception.GetType().Name);
            }
        }
    }
}