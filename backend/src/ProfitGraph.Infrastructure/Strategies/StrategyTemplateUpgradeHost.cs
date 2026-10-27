using MediatR;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.UpgradeStrategyTemplates;

namespace ProfitGraph.Infrastructure.Strategies;

/// <summary>
///     Brings saved strategies forward to the later revision of their template, shortly after the Worker's <c>live</c> role starts and
///     then once an hour (ADR 0038). Each upgrade is a new version of the strategy that only adds blocks; running bots follow it on their
///     next root (ADR 0019). <c>Strategies:TemplateUpgrades:Enabled</c> (default true) switches it off.
/// </summary>
internal sealed class StrategyTemplateUpgradeHost(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<StrategyTemplateUpgradeHost> logger) : BackgroundService
{
    private static readonly TimeSpan FirstPass = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Strategies:TemplateUpgrades:Enabled", defaultValue: true))
        {
            return;
        }

        try
        {
            await Task.Delay(FirstPass, clock, stoppingToken);
            using var timer = new PeriodicTimer(Interval, clock);

            do
            {
                await Upgrade(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    private async Task Upgrade(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            IReadOnlyList<UpgradedStrategyInternal> upgraded = await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new UpgradeStrategyTemplatesCommand(), cancellationToken);

            foreach (UpgradedStrategyInternal strategy in upgraded)
            {
                logger.LogInformation(
                    "Strategy {StrategyId} was upgraded to version {Version} (a filled manual order rebuilds the grid, ADR 0037); " +
                    "{RunningBots} bot(s) that should run follow it.",
                    strategy.StrategyId,
                    strategy.Version,
                    strategy.RunningBots);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Upgrading the strategies' templates failed with {ExceptionType}; it is tried again.", exception.GetType().Name);
        }
    }
}
