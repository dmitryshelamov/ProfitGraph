using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Notifications;
using ProfitGraph.Application.Notifications.ClaimTelegramNotifications;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.Ports;
using ProfitGraph.Application.Notifications.PruneTelegramNotifications;
using ProfitGraph.Application.Notifications.RecordTelegramDelivery;

namespace ProfitGraph.Infrastructure.Notifications;

/// <summary>
///     Sends queued Telegram notifications in every process of the Worker's <c>live</c> role (ADR 0042), apart from polling
///     and without a lease. Each pass claims due notifications under its own claim, sends them outside any transaction
///     within Telegram's limits and records each answer; a transport failure is tried again later. A process that dies
///     leaves its claimed notifications to another after the claim expires. Finished notifications are pruned every
///     <see cref="TelegramOptions.PruneInterval" />.
/// </summary>
internal sealed class TelegramDeliveryHost(IServiceScopeFactory scopes, IOptions<TelegramOptions> options, TimeProvider clock, ILogger<TelegramDeliveryHost> logger)
    : BackgroundService
{
    private readonly TelegramRateLimiter _limits = new TelegramRateLimiter(options.Value, clock);
    private DateTimeOffset _nextPruneAt = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = false;

            try
            {
                await PruneWhenDue(stoppingToken);
                claimed = await Pass(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("The Telegram delivery pass failed with {ExceptionType}; it is tried again.", exception.GetType().Name);
            }

            if (!claimed)
            {
                try
                {
                    await Task.Delay(options.Value.DeliveryInterval, clock, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    ///     Claims and sends one batch; false when nothing was due.
    /// </summary>
    private async Task<bool> Pass(CancellationToken stoppingToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var telegram = scope.ServiceProvider.GetRequiredService<ITelegramClient>();
        var claimId = Guid.NewGuid();
        TelegramClaimInternal claim = await sender.Send(new ClaimTelegramNotificationsCommand(claimId, options.Value.DeliveryBatchSize), stoppingToken);

        foreach (TelegramOutgoingInternal message in claim.Messages)
        {
            await _limits.WaitTurn(message.ChatId, stoppingToken);
            TelegramSendResult result = await Send(telegram, message, stoppingToken);
            await sender.Send(
                new RecordTelegramDeliveryCommand(message.NotificationId, claimId, message.ChatId, result.Outcome, result.RetryAfter, result.Error),
                stoppingToken);

            if (result.Outcome != TelegramSendOutcome.Sent)
            {
                logger.LogWarning("A Telegram notification was not sent: {Outcome} {Error}.", result.Outcome, result.Error);
            }
        }

        return claim.Claimed > 0;
    }

    private static async Task<TelegramSendResult> Send(ITelegramClient telegram, TelegramOutgoingInternal message, CancellationToken stoppingToken)
    {
        try
        {
            return await telegram.SendMessage(message.ChatId, message.Text, message.Buttons, stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            return new TelegramSendResult(TelegramSendOutcome.RetryLater, Error: exception.GetType().Name);
        }
    }

    private async Task PruneWhenDue(CancellationToken stoppingToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        if (now < _nextPruneAt)
        {
            return;
        }

        _nextPruneAt = now + options.Value.PruneInterval;
        using IServiceScope scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new PruneTelegramNotificationsCommand(), stoppingToken);
    }
}