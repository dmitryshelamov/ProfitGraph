using FluentValidation;

using ProfitGraph.Application.Strategies.Validators;

namespace ProfitGraph.Application.Strategies.ValidateStrategy;

internal sealed class ValidateStrategyQueryValidator : AbstractValidator<ValidateStrategyQuery>
{
    public ValidateStrategyQueryValidator()
    {
        RuleFor(query => query.Id).NotEmpty();
        RuleFor(query => query.Version).GreaterThan(0).When(query => query.Version.HasValue);
        RuleFor(query => query.Version)
            .Null()
            .When(query => query.Definition is not null)
            .WithMessage("A draft definition cannot be combined with a saved version.");
        RuleFor(query => query.Definition!).SetValidator(new StrategyDefinitionInternalValidator()).When(query => query.Definition is not null);
    }
}