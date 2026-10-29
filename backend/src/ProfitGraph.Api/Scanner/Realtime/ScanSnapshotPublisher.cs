using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

using ProfitGraph.Api.Common.Realtime;
using ProfitGraph.Application.Scanner.Ports;

namespace ProfitGraph.Api.Scanner.Realtime;

/// <summary>
///     Turns the scanner's snapshot signals into <c>ScanSnapshotChanged</c> for every client of <see cref="ScannerHub" />.
/// </summary>
public sealed class ScanSnapshotPublisher(
    IScanSnapshotNotifications notifications,
    IHubContext<ScannerHub> hub,
    IOptions<RealtimeOptions> options,
    ILogger<ScanSnapshotPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (Guid _ in notifications.Listen(stoppingToken))
                {
                    await hub.Clients.All.SendAsync(ScannerHub.SnapshotChanged, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Listening for scanner snapshots failed; listening again.");
                await Task.Delay(options.Value.RetryDelay, stoppingToken);
            }
        }
    }
}