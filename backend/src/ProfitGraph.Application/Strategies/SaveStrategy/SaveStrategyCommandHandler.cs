using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.SaveStrategy;

internal sealed class SaveStrategyCommandHandler(IStrategyRepository repository, IBotRepository bots, TimeProvider clock)
    : IRequestHandler<SaveStrategyCommand, StrategyInternal>
{
    private const string ChangedCode = "strategy_changed";

    public async Task<StrategyInternal> Handle(SaveStrategyCommand request, CancellationToken cancellationToken)
    {
        Strategy strategy = await repository.GetOwned(request.Id, request.UserId, cancellationToken);
        var expectedVersion = new StrategyVersionNumber(request.ExpectedVersion);

        if (strategy.CurrentVersion != expectedVersion)
        {
            throw new StateConflictException("The strategy has changed. Reload it before saving.", ChangedCode);
        }

        if (!request.ConfirmLiveImpact && (await bots.ListDesiredRunningOnStrategy(strategy.Id, cancellationToken)).Count > 0)
        {
            throw new StateConflictException("Running bots switch to the new version; confirm the live impact to save.", LiveImpact.ConflictCode);
        }

        StrategyVersion version = strategy.Save(new StrategyName(request.Name), request.Definition.ToDomain(), request.Layout.ToDomain(), clock.GetUtcNow());

        if (!await repository.Save(strategy, version, expectedVersion, cancellationToken))
        {
            throw new StateConflictException("The strategy has changed. Reload it before saving.", ChangedCode);
        }

        return strategy.ToInternal(version);
    }
}