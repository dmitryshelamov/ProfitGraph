using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Ports;
using ProfitGraph.Application.Notifications.Updates;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Infrastructure.Notifications;

/// <summary>
///     Reads the Telegram bot's updates in the Worker's <c>live</c> role (ADR 0020, ADR 0042). The process that holds the
///     poller lease reads updates from the stored offset, hands each to <see cref="TelegramUpdateProcessor" /> and advances
///     the offset.
///     Notifications are sent by <see cref="TelegramDeliveryHost" />, so they never wait for a long poll.
/// </summary>
internal sealed class TelegramPollingHost(IServiceScopeFactory scopes, IOptions<TelegramOptions> options, TimeProvider clock, ILogger<TelegramPollingHost> logger)
    : BackgroundService
{
    private readonly Guid _holder = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var leading = false;

            try
            {
                leading = await Pass(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("The Telegram pass failed with {ExceptionType}; it is tried again.", exception.GetType().Name);
            }

            if (!leading)
            {
                try
                {
                    await Task.Delay(options.Value.RetryDelay, clock, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    ///     One poll; false when another process leads. Each update is processed in its own scope; one that fails is logged
    ///     and passed, so it cannot stop the bot.
    /// </summary>
    private async Task<bool> Pass(CancellationToken stoppingToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        var leases = scope.ServiceProvider.GetRequiredService<ITelegramPollerLeaseRepository>();
        var telegram = scope.ServiceProvider.GetRequiredService<ITelegramClient>();

        if (await leases.Acquire(_holder, clock.GetUtcNow(), options.Value.PollerLeaseDuration, stoppingToken) is not { } offset)
        {
            return false;
        }

        foreach (TelegramUpdateInternal update in await telegram.GetUpdates(offset, options.Value.PollTimeout, stoppingToken))
        {
            await Process(update, stoppingToken);
            await leases.Advance(_holder, update.UpdateId + 1, clock.GetUtcNow(), stoppingToken);
        }

        return true;
    }

    private async Task Process(TelegramUpdateInternal update, CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TelegramUpdateProcessor>().Process(update, stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("A Telegram update failed with {ExceptionType}; it is passed.", exception.GetType().Name);
        }
    }
}