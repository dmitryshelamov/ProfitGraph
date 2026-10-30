using FluentValidation;

namespace ProfitGraph.Application.Scanner.SaveScanAutoTradeRule;

internal sealed class SaveScanAutoTradeRuleCommandValidator : AbstractValidator<SaveScanAutoTradeRuleCommand>
{
    public SaveScanAutoTradeRuleCommandValidator(ScanTradeOptions options)
    {
        RuleFor(command => command.Direction).IsInEnum();
        RuleFor(command => command.Scenario).IsInEnum();
        RuleFor(command => command.Timeframe)
            .Must(timeframe =>
                options.LevelTimeframes.Contains(timeframe) || timeframe is ScanTradeIdeasReader.PullbackTimeframe or ScanTradeIdeasReader.ExtremeTimeframe)
            .WithMessage("The timeframe is not one the cards are drawn on.");
        RuleFor(command => command.MinChance).InclusiveBetween(from: 0m, to: 1m).When(command => command.MinChance is not null);
        RuleFor(command => command.MaxOpenTrades).InclusiveBetween(from: 1, to: 100).When(command => command.MaxOpenTrades is not null);
        RuleFor(command => command.MaxTradesPerDay).InclusiveBetween(from: 1, to: 1000).When(command => command.MaxTradesPerDay is not null);
        RuleFor(command => command.DailyLossLimit).GreaterThan(0m).When(command => command.DailyLossLimit is not null);
    }
}