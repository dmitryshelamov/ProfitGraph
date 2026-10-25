using Microsoft.AspNetCore.SignalR;

using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Api.Bots.Realtime;

/// <summary>
///     Turns the Worker's price signals into <c>LivePrice</c> messages for the instrument's group (S5-19). A signal is
///     read back from storage, so a message always carries the latest stored price.
/// </summary>
public sealed class LivePricePublisher(
    ILiveMarkPriceNotifications notifications,
    IHubContext<LivePriceHub> hub,
    IServiceScopeFactory scopes,
    ILogger<LivePricePublisher> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await foreach (Guid signal in notifications.Listen(stoppingToken))
                {
                    await Publish(signal, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Listening for display prices failed; listening again.");
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task Publish(Guid signal, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        if (await scope.ServiceProvider.GetRequiredService<ILiveMarkPriceRepository>().GetBySignal(signal, cancellationToken) is { } price)
        {
            await hub.Clients.Group(LivePriceHub.Group(price.Venue.Value, price.Symbol.Value))
                .SendAsync(
                    "LivePrice",
                    new LivePriceResponse(price.Venue.Value, price.Symbol.Value, ExactGraphNumberParser.Format(price.Price), price.ObservedAt),
                    cancellationToken);
        }
    }
}