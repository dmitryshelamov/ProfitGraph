using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.RenameStrategy;

internal sealed class RenameStrategyCommandHandler(IStrategyRepository repository, TimeProvider clock) : IRequestHandler<RenameStrategyCommand, StrategyInternal>
{
    public async Task<StrategyInternal> Handle(RenameStrategyCommand command, CancellationToken cancellationToken)
    {
        Strategy strategy = await repository.GetOwned(command.Id, command.UserId, cancellationToken);

        if (strategy.Rename(new StrategyName(command.Name), clock.GetUtcNow()))
        {
            await repository.Rename(strategy, cancellationToken);
        }

        StrategyVersion version = await repository.GetVersion(strategy.Id, strategy.CurrentVersion, cancellationToken)
            ?? throw new ResourceNotFoundException("The strategy version does not exist.", "strategy_version_not_found");

        return strategy.ToInternal(version);
    }
}