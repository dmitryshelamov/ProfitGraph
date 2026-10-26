using FluentValidation;

using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Strategies.RenameStrategy;

internal sealed class RenameStrategyCommandValidator : AbstractValidator<RenameStrategyCommand>
{
    public RenameStrategyCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(StrategyName.MaxLength);
    }
}