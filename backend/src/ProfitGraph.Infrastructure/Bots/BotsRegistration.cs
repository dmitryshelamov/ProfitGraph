using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Bots;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Bots.Repositories;

namespace ProfitGraph.Infrastructure.Bots;

internal static class BotsRegistration
{
    internal static IServiceCollection AddBots(this IServiceCollection services)
    {
        services.AddScoped<IBotRepository, BotRepository>();
        services.AddScoped<IBotLeaseRepository, BotLeaseRepository>();
        services.AddScoped<IBotCheckpointRepository, BotCheckpointRepository>();
        services.AddScoped<IOrderIntentRepository, OrderIntentRepository>();
        services.AddScoped<ITradingHaltRepository, TradingHaltRepository>();
        services.AddScoped<BotRootCommitter>();

        return services;
    }
}