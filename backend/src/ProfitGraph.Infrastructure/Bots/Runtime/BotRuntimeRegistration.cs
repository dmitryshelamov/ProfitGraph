using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Infrastructure.Accounts;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

public static class BotRuntimeRegistration
{
    /// <summary>
    ///     Runs live bots in this process: the Worker's <c>live</c> role. Bots trade only through an order gateway, which a
    ///     venue module registers only when trading is enabled.
    /// </summary>
    public static IServiceCollection AddBotRuntime(this IServiceCollection services)
    {
        services.AddScoped<BotEngineSessionFactory>();
        services.AddScoped<BotMarketDataAccess>();
        services.AddScoped<BotReconciler>();
        services.AddScoped<BotGraphFollower>();
        services.AddSingleton<LiveRuntimeIdentity>();
        services.AddSingleton<IHostedService, BotRuntimeHost>();
        services.AddSingleton<IHostedService, OrderIntentDispatchHost>();
        services.AddSingleton<IHostedService, BotFeedRetentionHost>();
        services.AddSingleton<IHostedService, LiveMarkPricePublisher>();
        services.AddSingleton<IHostedService, AccountSnapshotHost>();

        return services;
    }
}