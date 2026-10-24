using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Common.Ports;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Application.Strategies.ValidateStrategy;

internal sealed class ValidateStrategyQueryHandler(IStrategyRepository repository, INodeTypeRegistry registry)
    : IRequestHandler<ValidateStrategyQuery, StrategyValidationInternal>
{
    public async Task<StrategyValidationInternal> Handle(ValidateStrategyQuery request, CancellationToken cancellationToken)
    {
        Strategy strategy = await repository.GetOwned(request.Id, request.UserId, cancellationToken);
        int? checkedVersion = null;
        StrategyDefinition definition;

        if (request.Definition is null)
        {
            StrategyVersion version = await repository.GetVersion(
                    strategy.Id,
                    request.Version is { } number ? new StrategyVersionNumber(number) : strategy.CurrentVersion,
                    cancellationToken)
                ?? throw new ResourceNotFoundException("The strategy version does not exist.", "strategy_version_not_found");
            checkedVersion = version.Number.Value;
            definition = version.Definition;
        }
        else
        {
            definition = request.Definition.ToDomain();
        }

        StrategyBlockValidationResult result = new StrategyDefinitionValidator().Validate(definition, registry.GetAll());

        return new StrategyValidationInternal(
            result.IsValid,
            checkedVersion,
            result.Diagnostics.Select(diagnostic => new StrategyDiagnosticInternal(
                    diagnostic.Code.ToString(),
                    diagnostic.Message,
                    diagnostic.BlockId,
                    diagnostic.ValueName,
                    diagnostic.ParameterName,
                    CheckIndex: diagnostic.CheckIndex))
                .ToArray());
    }
}