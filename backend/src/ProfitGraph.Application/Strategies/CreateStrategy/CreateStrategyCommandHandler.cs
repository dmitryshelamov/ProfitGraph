using MediatR;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.CreateStrategy;

internal sealed class CreateStrategyCommandHandler(IStrategyRepository repository, TimeProvider clock) : IRequestHandler<CreateStrategyCommand, StrategyInternal>
{
    public async Task<StrategyInternal> Handle(CreateStrategyCommand request, CancellationToken cancellationToken)
    {
        (Strategy strategy, StrategyVersion version) = Strategy.Create(
            Guid.NewGuid(),
            request.UserId,
            request.Name,
            request.Definition.ToDomain(),
            request.Layout.ToDomain(),
            clock.GetUtcNow());
        await repository.Add(strategy, version, cancellationToken);

        return strategy.ToInternal(version);
    }
}