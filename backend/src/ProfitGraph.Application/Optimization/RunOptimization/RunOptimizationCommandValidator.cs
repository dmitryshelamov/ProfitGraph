using FluentValidation;

namespace ProfitGraph.Application.Optimization.RunOptimization;

internal sealed class RunOptimizationCommandValidator : AbstractValidator<RunOptimizationCommand>
{
    public RunOptimizationCommandValidator()
    {
        RuleFor(command => command.OptimizationId).NotEmpty();
    }
}