using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Common.Outbox;

namespace ProfitGraph.Infrastructure.Common.Outbox;

public static class OutboxRegistration
{
    /// <summary>
    ///     Delivers stored Domain events in this process and deletes delivered ones after the retention. Only the Worker calls
    ///     it; every process writes events.
    /// </summary>
    public static IServiceCollection AddOutboxDispatcher(this IServiceCollection services)
    {
        services.AddSingleton<IHostedService, OutboxDispatcher>();
        services.AddSingleton<IHostedService, OutboxRetentionHost>();

        return services;
    }

    internal static IServiceCollection AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .Validate(
                options => options.IsValid(),
                "Outbox:PollingInterval, Outbox:MaxRetryDelay, Outbox:MaxAttempts, Outbox:Retention and Outbox:RetentionBatchSize must be positive.")
            .ValidateOnStart();
        services.AddSingleton<OutboxEventTypes>();
        services.AddScoped<OutboxWriter>();
        services.AddScoped<OutboxDelivery>();
        services.AddScoped<IOutboxStore, OutboxStore>();
        services.AddHealthChecks().AddCheck<OutboxHealthCheck>(OutboxHealthCheck.Name, tags: [HealthTags.Ready]);

        return services;
    }
}