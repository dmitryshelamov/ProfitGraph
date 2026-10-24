using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Execution;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Application.Optimization.RunOptimization;
using ProfitGraph.Domain.Optimization;
using ProfitGraph.Infrastructure.Jobs;
using ProfitGraph.Infrastructure.Optimization.Repositories;

namespace ProfitGraph.Infrastructure.Optimization;

internal static class OptimizationRegistration
{
    /// <summary>
    ///     Registers optimizations, their trial store and the optimization job, which runs in its own Worker lane.
    /// </summary>
    internal static IServiceCollection AddOptimization(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OptimizationTrialRunnerOptions>()
            .Bind(configuration.GetSection(OptimizationTrialRunnerOptions.SectionName))
            .Validate(options => options.IsValid(), "Optimization:MaxWorkers must be 1..256, ChannelCapacity positive and BatchSize 1..10000.")
            .ValidateOnStart();
        services.AddScoped(provider => provider.GetRequiredService<IOptionsMonitor<OptimizationTrialRunnerOptions>>().CurrentValue);
        services.AddScoped<OptimizationTrialRunner>();
        services.AddScoped<IOptimizationRunRepository, OptimizationRunRepository>();
        services.AddScoped<IOptimizationTrialStore, OptimizationTrialStore>();
        services.AddScoped<IOptimizationWindowStore, OptimizationWindowStore>();
        services.AddScoped<WalkForwardRunner>();
        services.AddScoped<IOptimizationReader, OptimizationReader>();
        services.AddSingleton<IOptimizationProgressNotifications, OptimizationProgressNotifications>();
        services.AddBackgroundJob<RunOptimizationCommand, OptimizationCompletedInternal>(BackgroundJobLanes.Optimization, resumable: true);

        return services;
    }
}