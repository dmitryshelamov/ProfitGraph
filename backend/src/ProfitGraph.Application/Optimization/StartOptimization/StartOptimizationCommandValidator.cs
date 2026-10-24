using FluentValidation;

using ProfitGraph.Application.Backtesting.StartBacktest;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Application.Optimization.StartOptimization;

internal sealed class StartOptimizationCommandValidator : AbstractValidator<StartOptimizationCommand>
{
    private const int MaxRanges = 20;

    public StartOptimizationCommandValidator(VenueRegistry venues, VenuePorts<IInstrumentRulesSource> rules)
    {
        RuleFor(command => command.Backtest).SetValidator(new StartBacktestCommandValidator(venues, rules));
        RuleFor(command => command.Ranges)
            .Must(ranges => ranges.Count is > 0 and <= MaxRanges && ranges.Keys.All(name => !string.IsNullOrWhiteSpace(name)))
            .WithMessage($"Give 1 to {MaxRanges} ranges by parameter name.");
        RuleFor(command => command.Method)
            .Must(method => OptimizationNames.Methods.Contains(method))
            .When(command => command.Method is not null)
            .WithMessage($"Use one of: {string.Join(", ", OptimizationNames.Methods)}.");
        RuleFor(command => command.Objective)
            .Must(objective => OptimizationNames.Objectives.Contains(objective))
            .When(command => command.Objective is not null)
            .WithMessage($"Use one of: {string.Join(", ", OptimizationNames.Objectives)}.");
        RuleFor(command => command.Budget).InclusiveBetween(from: 1, OptimizationSearchSpace.MaximumBudget).When(command => command.Budget is not null);
        RuleFor(command => command.FinalistCount)
            .InclusiveBetween(from: 0, OptimizationConfiguration.MaximumFinalists)
            .When(command => command.FinalistCount is not null);
        RuleFor(command => command.MinExecutions).GreaterThanOrEqualTo(0).When(command => command.MinExecutions is not null);
        RuleFor(command => command.WalkForward!.Scheme)
            .Must(scheme => scheme is "rolling" or "anchored")
            .When(command => command.WalkForward is not null)
            .WithMessage("Use rolling or anchored.");
        RuleFor(command => command.MaxDrawdownPercent).InclusiveBetween(from: 0m, to: 100m).When(command => command.MaxDrawdownPercent is not null);
    }
}