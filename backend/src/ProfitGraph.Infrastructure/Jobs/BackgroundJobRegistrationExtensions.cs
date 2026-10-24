using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Jobs.Ports;
using ProfitGraph.Infrastructure.Common;
using ProfitGraph.Infrastructure.Jobs.Repositories;

namespace ProfitGraph.Infrastructure.Jobs;

public static class BackgroundJobRegistrationExtensions
{
    /// <summary>
    ///     Registers a kind of work so that its request and result can be stored and read back, the Worker lane that runs it
    ///     and whether it is resumable: a resumable job that a Worker restart interrupts is queued again, and its handler
    ///     continues from the work it stored.
    /// </summary>
    public static IServiceCollection AddBackgroundJob<TRequest, TResult>(
        this IServiceCollection services,
        string lane = BackgroundJobLanes.Main,
        bool resumable = false)
        where TRequest : IBackgroundJobRequest<TResult>
        where TResult : IBackgroundJobResult
    {
        if (!BackgroundJobLanes.All.Contains(lane))
        {
            throw new ArgumentOutOfRangeException(nameof(lane), $"Unknown Worker lane '{lane}'.");
        }

        services.AddSingleton(new BackgroundJobRegistration(TRequest.Kind, typeof(TRequest), typeof(TResult), lane, resumable));

        return services;
    }

    /// <summary>
    ///     Runs queued jobs in this process. Only the Worker calls it; the API only queues and reads jobs.
    /// </summary>
    public static IServiceCollection AddBackgroundJobRunner(this IServiceCollection services)
    {
        services.AddScoped<BackgroundJobRunner>();

        foreach (string lane in BackgroundJobLanes.All)
        {
            services.AddSingleton<IHostedService>(provider => ActivatorUtilities.CreateInstance<BackgroundJobWorker>(provider, lane));
        }

        return services;
    }

    internal static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BackgroundJobOptions>()
            .Bind(configuration.GetSection(BackgroundJobOptions.SectionName))
            .Validate(options => options.IsValid(), "BackgroundJobs:PollingInterval must be positive.")
            .ValidateOnStart();
        services.AddSingleton<BackgroundJobPayloadSerializer>();
        services.AddScoped<BackgroundJobRepository>();
        services.AddScoped<IBackgroundJobRepository>(provider => provider.GetRequiredService<BackgroundJobRepository>());
        services.AddScoped<BackgroundJobProgressReporter>();
        services.AddScoped<IBackgroundJobProgress>(provider => provider.GetRequiredService<BackgroundJobProgressReporter>());
        services.AddScoped<BackgroundJobQueue>();
        services.AddHealthChecks().AddCheck<WorkerHeartbeatHealthCheck>(WorkerHeartbeatHealthCheck.Name, tags: [HealthTags.Ready]);

        return services;
    }
}