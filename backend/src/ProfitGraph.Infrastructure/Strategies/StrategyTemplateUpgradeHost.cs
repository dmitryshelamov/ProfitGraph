using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.UpgradeStrategyTemplates;

namespace ProfitGraph.Infrastructure.Strategies;

/// <summary>
///     Brings saved strategies forward to the later revision of their template, once, shortly after the Worker's <c>live</c> role
///     starts, and again after <see cref="StrategyTemplateUpgradeOptions.RetryDelay" /> when the pass failed, up to
///     <see cref="StrategyTemplateUpgradeOptions.MaxPasses" /> passes (ADR 0038). A strategy saved later is made from the
///     current template, which has the blocks already; one that is not (an old copy) is upgraded at the next start. Each upgrade is a
///     new version of the strategy that only adds blocks; running bots follow it on their next root (ADR 0019).
///     <c>Strategies:TemplateUpgrades:Enabled</c> (default true) switches it off.
/// </summary>
internal sealed class StrategyTemplateUpgradeHost(
    IServiceScopeFactory scopes,
    IOptions<StrategyTemplateUpgradeOptions> options,
    TimeProvider clock,
    ILogger<StrategyTemplateUpgradeHost> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        try
        {
            await Task.Delay(options.Value.FirstPassDelay, clock, stoppingToken);

            for (var pass = 1; pass <= options.Value.MaxPasses && !await Upgrade(stoppingToken); pass++)
            {
                await Task.Delay(options.Value.RetryDelay, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    private async Task<bool> Upgrade(CancellationToken cancellationToken)
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

            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Upgrading the strategies' templates failed with {ExceptionType}; it is tried again after {RetryDelay}, up to {MaxPasses} passes.",
                exception.GetType().Name,
                options.Value.RetryDelay,
                options.Value.MaxPasses);

            return false;
        }
    }
}