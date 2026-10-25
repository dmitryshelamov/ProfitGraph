using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Notifications;
using ProfitGraph.Application.Notifications.BlockTelegramChat;
using ProfitGraph.Application.Notifications.Contracts;
using ProfitGraph.Application.Notifications.LinkTelegramChat;
using ProfitGraph.Application.Notifications.Ports;
using ProfitGraph.Domain.Notifications;

namespace ProfitGraph.Infrastructure.Notifications;

/// <summary>
///     The Telegram bot of the Worker's <c>live</c> role (ADR 0020). The process that holds the poller lease reads updates
///     from the stored offset, links chats that send <c>/start CODE</c> and advances the offset; then it sends alerts that
///     wait for linked chats and marks each sent only after Telegram accepted it, so a crash between the two sends it
///     again (at least once). A blocked chat receives nothing more.
/// </summary>
internal sealed class TelegramHost(IServiceScopeFactory scopes, IOptions<TelegramOptions> options, TimeProvider clock, ILogger<TelegramHost> logger)
    : BackgroundService
{
    private const int DeliveryBatch = 20;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

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
    ///     One poll and delivery round; false when another process leads or the round failed early.
    /// </summary>
    private async Task<bool> Pass(CancellationToken stoppingToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        var leases = scope.ServiceProvider.GetRequiredService<ITelegramPollerLeaseRepository>();
        var telegram = scope.ServiceProvider.GetRequiredService<ITelegramClient>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        if (await leases.Acquire(_holder, clock.GetUtcNow(), LeaseDuration, stoppingToken) is not { } offset)
        {
            return false;
        }

        foreach (TelegramUpdateInternal update in await telegram.GetUpdates(offset, options.Value.PollTimeout, stoppingToken))
        {
            if (update is { ChatId: { } chatId, Text: { } text })
            {
                await Answer(telegram, sender, chatId, text, stoppingToken);
            }

            await leases.Advance(_holder, update.UpdateId + 1, clock.GetUtcNow(), stoppingToken);
        }

        await Deliver(scope.ServiceProvider.GetRequiredService<ITelegramLinkRepository>(), telegram, sender, stoppingToken);

        return true;
    }

    private static async Task Answer(ITelegramClient telegram, ISender sender, long chatId, string text, CancellationToken stoppingToken)
    {
        string[] words = text.Split(separator: ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words is not ["/start", var code, ..])
        {
            await telegram.SendMessage(chatId, TelegramMessages.Help, stoppingToken);

            return;
        }

        bool linked = await sender.Send(new LinkTelegramChatCommand(code, chatId), stoppingToken);
        await telegram.SendMessage(chatId, linked ? TelegramMessages.Linked : TelegramMessages.UnknownCode, stoppingToken);
    }

    private async Task Deliver(ITelegramLinkRepository links, ITelegramClient telegram, ISender sender, CancellationToken stoppingToken)
    {
        foreach (TelegramAlertDelivery alert in await links.ListUndelivered(DeliveryBatch, stoppingToken))
        {
            if (await telegram.SendMessage(alert.ChatId, TelegramMessages.Alert(alert), stoppingToken) == TelegramSendOutcome.Blocked)
            {
                await sender.Send(new BlockTelegramChatCommand(alert.ChatId), stoppingToken);
                logger.LogWarning("A Telegram chat blocked the bot; it receives no more alerts.");

                continue;
            }

            await links.MarkDelivered(alert.AlertId, clock.GetUtcNow(), stoppingToken);
        }
    }
}