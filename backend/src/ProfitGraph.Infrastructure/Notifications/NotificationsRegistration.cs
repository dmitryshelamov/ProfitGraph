using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Notifications.Ports;

namespace ProfitGraph.Infrastructure.Notifications;

public static class NotificationsRegistration
{
    /// <summary>
    ///     Runs the Telegram bot in this process, the Worker's <c>live</c> role, only when <c>Telegram:Enabled</c> is true;
    ///     otherwise nothing is registered and nothing starts (ADR 0020).
    /// </summary>
    public static IServiceCollection AddTelegramNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>($"{TelegramOptions.SectionName}:{nameof(TelegramOptions.Enabled)}"))
        {
            return services;
        }

        services.AddHttpClient<ITelegramClient, TelegramHttpClient>((provider, client) =>
            {
                client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<TelegramOptions>>().Value.BaseAddress);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .RemoveAllLoggers();
        services.AddSingleton<IHostedService, TelegramHost>();

        return services;
    }
}