using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Execution;
using ProfitGraph.Application.Backtesting.History;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Application.Backtesting.RunBacktest;
using ProfitGraph.Domain.Backtesting;
using ProfitGraph.Infrastructure.Backtesting.Repositories;
using ProfitGraph.Infrastructure.Jobs;

namespace ProfitGraph.Infrastructure.Backtesting;

public static class BacktestingRegistration
{
    /// <summary>
    ///     Registers runs, their fact store and the backtest job that the Worker executes.
    /// </summary>
    internal static IServiceCollection AddBacktesting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BacktestExecutionOptions>()
            .Bind(configuration.GetSection(BacktestExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), "Backtests:BatchSize must be 1..10000 and Backtests:ChannelCapacity at least the batch size.")
            .ValidateOnStart();
        services.AddScoped(provider => provider.GetRequiredService<IOptionsMonitor<BacktestExecutionOptions>>().CurrentValue);
        services.AddScoped<BacktestCompiler>();
        services.AddScoped<BacktestHistoryLoader>();
        services.AddScoped<IBacktestRunRepository, BacktestRunRepository>();
        services.AddScoped<IBacktestFactStore, BacktestFactStore>();
        services.AddScoped<IBacktestReader, BacktestReader>();
        services.AddSingleton<IBacktestProgressNotifications, BacktestProgressNotifications>();
        services.AddBackgroundJob<RunBacktestCommand, BacktestCompletedInternal>();

        return services;
    }

    /// <summary>
    ///     Adds compaction of finished runs to the Worker.
    /// </summary>
    public static IServiceCollection AddBacktestCompaction(this IServiceCollection services)
    {
        services.AddScoped<BacktestCompactor>();
        services.AddHostedService<BacktestCompactionWorker>();

        return services;
    }
}