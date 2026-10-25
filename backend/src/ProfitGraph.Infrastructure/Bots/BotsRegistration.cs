using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Bots.Repositories;
using ProfitGraph.Infrastructure.Bots.Runtime;
using ProfitGraph.Infrastructure.Common;

namespace ProfitGraph.Infrastructure.Bots;

internal static class BotsRegistration
{
    internal static IServiceCollection AddBots(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BotRuntimeOptions>()
            .Bind(configuration.GetSection(BotRuntimeOptions.SectionName))
            .Validate(options => options.IsValid(), "Bots:Runtime needs positive intervals and a lease longer than two renewals.")
            .ValidateOnStart();
        services.AddHealthChecks().AddCheck<LiveRuntimeHealthCheck>(LiveRuntimeHealthCheck.Name, tags: [HealthTags.Ready]);
        services.AddScoped<IBotRepository, BotRepository>();
        services.AddScoped<IBotLeaseRepository, BotLeaseRepository>();
        services.AddScoped<IBotCheckpointRepository, BotCheckpointRepository>();
        services.AddScoped<IOrderIntentRepository, OrderIntentRepository>();
        services.AddScoped<ITradingHaltRepository, TradingHaltRepository>();
        services.AddScoped<IBotAlertRepository, BotAlertRepository>();
        services.AddScoped<IManualOrderRequestRepository, ManualOrderRequestRepository>();
        services.AddScoped<IBotFillRepository, BotFillRepository>();
        services.AddScoped<IBotFeeCorrectionRepository, BotFeeCorrectionRepository>();
        services.AddScoped<IBotPositionCycleRepository, BotPositionCycleRepository>();
        services.AddScoped<IBotFeedRepository, BotFeedRepository>();
        services.AddScoped<ILiveMarkPriceRepository, LiveMarkPriceRepository>();
        services.AddSingleton<ILiveMarkPriceNotifications, LiveMarkPriceNotifications>();
        services.AddScoped<BotRootCommitter>();
        services.AddScoped<BotKillSwitch>();
        services.AddSingleton<BotUpdateBus>();

        return services;
    }
}