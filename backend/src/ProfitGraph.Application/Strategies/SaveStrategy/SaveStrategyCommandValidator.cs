using FluentValidation;

using ProfitGraph.Application.Strategies.Validators;

namespace ProfitGraph.Application.Strategies.SaveStrategy;

internal sealed class SaveStrategyCommandValidator : AbstractValidator<SaveStrategyCommand>
{
    public SaveStrategyCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Definition).NotNull().SetValidator(new StrategyDefinitionInternalValidator());
        RuleFor(command => command.Layout).NotNull().SetValidator(new GraphLayoutInternalValidator());
    }
}