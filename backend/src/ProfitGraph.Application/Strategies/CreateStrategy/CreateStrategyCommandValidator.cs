using FluentValidation;

using ProfitGraph.Application.Strategies.Validators;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.CreateStrategy;

internal sealed class CreateStrategyCommandValidator : AbstractValidator<CreateStrategyCommand>
{
    public CreateStrategyCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(StrategyName.MaxLength);
        RuleFor(command => command.Definition).NotNull().SetValidator(new StrategyDefinitionInternalValidator());
        RuleFor(command => command.Layout).NotNull().SetValidator(new GraphLayoutInternalValidator());
    }
}