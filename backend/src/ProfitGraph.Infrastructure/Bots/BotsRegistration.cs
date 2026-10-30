using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.DispatchOrderIntent;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Alerts;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.FeeCorrections;
using ProfitGraph.Domain.Bots.Feed;
using ProfitGraph.Domain.Bots.Fills;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Domain.Bots.ManualOrders;
using ProfitGraph.Domain.Bots.MarkPrices;
using ProfitGraph.Domain.Bots.OrderCancels;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Domain.Bots.OrderWaiting;
using ProfitGraph.Domain.Bots.PositionCloses;
using ProfitGraph.Domain.Bots.Positions;
using ProfitGraph.Domain.Trading;
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
        services.AddOptions<OrderDispatchOptions>()
            .Bind(configuration.GetSection(OrderDispatchOptions.SectionName))
            .Validate(options => options.IsValid(), "Bots:Dispatch needs a positive candidate limit and a deferral ceiling no shorter than the floor.")
            .ValidateOnStart();
        services.AddScoped(provider => provider.GetRequiredService<IOptionsMonitor<OrderDispatchOptions>>().CurrentValue);
        services.AddHealthChecks().AddCheck<LiveRuntimeHealthCheck>(LiveRuntimeHealthCheck.Name, tags: [HealthTags.Ready]);
        services.AddScoped<IBotRepository, BotRepository>();
        services.AddScoped<BotGridRules>();
        services.AddScoped<IBotLeaseRepository, BotLeaseRepository>();
        services.AddScoped<IBotCheckpointRepository, BotCheckpointRepository>();
        services.AddScoped<IOrderIntentRepository, OrderIntentRepository>();
        services.AddScoped<ITradingHaltRepository, TradingHaltRepository>();
        services.AddScoped<IBotAlertRepository, BotAlertRepository>();
        services.AddScoped<IManualOrderRequestRepository, ManualOrderRequestRepository>();
        services.AddScoped<IBotPositionCloseRepository, BotPositionCloseRepository>();
        services.AddScoped<IOrderCancelRequestRepository, OrderCancelRequestRepository>();
        services.AddScoped<IBotFillRepository, BotFillRepository>();
        services.AddScoped<IBotFeeCorrectionRepository, BotFeeCorrectionRepository>();
        services.AddScoped<IBotOrderWaitingRepository, BotOrderWaitingRepository>();
        services.AddScoped<IBotPositionCycleRepository, BotPositionCycleRepository>();
        services.AddScoped<IBotFeedRepository, BotFeedRepository>();
        services.AddScoped<ILiveMarkPriceRepository, LiveMarkPriceRepository>();
        services.AddSingleton<ILiveMarkPriceNotifications, LiveMarkPriceNotifications>();
        services.AddScoped<BotRootCommitter>();
        services.AddScoped<BotKillSwitch>();
        services.AddScoped<BotMarketDataAccess>();
        services.AddSingleton<BotUpdateBus>();

        return services;
    }
}