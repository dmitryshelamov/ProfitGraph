using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Ports;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Application.Strategies.UpgradeStrategyTemplates;

internal sealed class UpgradeStrategyTemplatesCommandHandler(IStrategyRepository repository, IBotRepository bots, INodeTypeRegistry registry, TimeProvider clock)
    : IRequestHandler<UpgradeStrategyTemplatesCommand, IReadOnlyList<UpgradedStrategyInternal>>
{
    public async Task<IReadOnlyList<UpgradedStrategyInternal>> Handle(UpgradeStrategyTemplatesCommand request, CancellationToken cancellationToken)
    {
        var upgraded = new List<UpgradedStrategyInternal>();

        foreach (Strategy strategy in await repository.ListAll(cancellationToken))
        {
            StrategyVersion? current = await Current(strategy, cancellationToken);

            // An upgraded graph that does not validate is left out: the strategy keeps the graph it has.
            if (current is null || StrategyTemplateUpgrades.WithManualFillRebuild(current.Definition) is not { } definition ||
                !new StrategyDefinitionValidator().Validate(definition, registry.GetAll()).IsValid)
            {
                continue;
            }

            int running = (await bots.ListDesiredRunningOnStrategy(strategy.Id, cancellationToken)).Count;
            StrategyVersionNumber expected = strategy.CurrentVersion;
            StrategyVersion version = strategy.Save(strategy.Name, definition, current.Layout, clock.GetUtcNow());

            // A strategy saved by its owner meanwhile is upgraded on the next pass.
            if (await repository.Save(strategy, version, expected, cancellationToken))
            {
                upgraded.Add(new UpgradedStrategyInternal(strategy.Id.Value, version.Number.Value, running));
            }
        }

        return upgraded;
    }

    /// <summary>
    ///     The strategy's current version, or null when it cannot be read: a strategy stored in a format the application no longer
    ///     reads is left as it is and does not stop the others from being upgraded.
    /// </summary>
    private async Task<StrategyVersion?> Current(Strategy strategy, CancellationToken cancellationToken)
    {
        try
        {
            return await repository.GetVersion(strategy.Id, strategy.CurrentVersion, cancellationToken);
        }
        catch (StateConflictException)
        {
            return null;
        }
    }
}
