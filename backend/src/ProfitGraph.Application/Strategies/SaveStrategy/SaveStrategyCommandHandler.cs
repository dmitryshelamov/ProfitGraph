using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.SaveStrategy;

internal sealed class SaveStrategyCommandHandler(IStrategyRepository repository, TimeProvider clock) : IRequestHandler<SaveStrategyCommand, StrategyInternal>
{
    private const string ChangedCode = "strategy_changed";

    public async Task<StrategyInternal> Handle(SaveStrategyCommand request, CancellationToken cancellationToken)
    {
        Strategy strategy = await repository.GetOwned(request.Id, request.UserId, cancellationToken);

        if (strategy.CurrentVersion != request.ExpectedVersion)
        {
            throw new StateConflictException("The strategy has changed. Reload it before saving.", ChangedCode);
        }

        StrategyVersion version = strategy.Save(request.Name, request.Definition.ToDomain(), request.Layout.ToDomain(), clock.GetUtcNow());

        if (!await repository.Save(strategy, version, request.ExpectedVersion, cancellationToken))
        {
            throw new StateConflictException("The strategy has changed. Reload it before saving.", ChangedCode);
        }

        return strategy.ToInternal(version);
    }
}