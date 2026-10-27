using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Notifications.Ports;
using ProfitGraph.Application.Notifications.Updates;

namespace ProfitGraph.Infrastructure.Notifications;

public static class NotificationsRegistration
{
    /// <summary>
    ///     Runs the Telegram bot in this process, the Worker's <c>live</c> role, only when <c>Telegram:Enabled</c> is true;
    ///     otherwise nothing is registered and nothing starts (ADR 0020). One host polls updates, another sends notifications
    ///     (ADR 0042).
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
        services.AddScoped<TelegramUpdateProcessor>();
        services.AddScoped<ITelegramMessageHandler, StartCommandHandler>();
        services.AddScoped<ITelegramMessageHandler, HelpCommandHandler>();
        services.AddScoped<ITelegramCallbackHandler, AcknowledgeAlertCallbackHandler>();
        services.AddSingleton<IHostedService, TelegramPollingHost>();
        services.AddSingleton<IHostedService, TelegramDeliveryHost>();

        return services;
    }
}