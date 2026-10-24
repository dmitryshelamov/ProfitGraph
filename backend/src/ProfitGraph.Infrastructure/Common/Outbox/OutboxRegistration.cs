using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ProfitGraph.Infrastructure.Common.Outbox;

public static class OutboxRegistration
{
    /// <summary>
    ///     Delivers stored Domain events in this process. Only the Worker calls it; every process writes events.
    /// </summary>
    public static IServiceCollection AddOutboxDispatcher(this IServiceCollection services)
    {
        services.AddSingleton<IHostedService, OutboxDispatcher>();

        return services;
    }

    internal static IServiceCollection AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .Validate(options => options.IsValid(), "Outbox:PollingInterval and Outbox:MaxRetryDelay must be positive.")
            .ValidateOnStart();
        services.AddSingleton<OutboxEventTypes>();
        services.AddScoped<OutboxWriter>();
        services.AddScoped<OutboxDelivery>();

        return services;
    }
}