using FluentValidation;

namespace ProfitGraph.Application.Backtesting.RunBacktest;

internal sealed class RunBacktestCommandValidator : AbstractValidator<RunBacktestCommand>
{
    public RunBacktestCommandValidator()
    {
        RuleFor(command => command.RunId).NotEmpty();
    }
}