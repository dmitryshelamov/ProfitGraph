using FluentValidation;

using ProfitGraph.Application.MarketData;

namespace ProfitGraph.Application.Scanner.CreateScanTrade;

internal sealed class CreateScanTradeCommandValidator : AbstractValidator<CreateScanTradeCommand>
{
    public CreateScanTradeCommandValidator(VenueRegistry venues)
    {
        this.ValidSeries(venues, command => command.Venue, command => command.Symbol, command => command.Timeframe);
        RuleFor(command => command.Scenario).IsInEnum().WithErrorCode("invalid_value").WithMessage("Unknown scenario.");
        RuleFor(command => command.Stop).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The stop is positive.");
        RuleFor(command => command.Entry).GreaterThan(command => command.Stop).WithErrorCode("invalid_value").WithMessage("The entry is above the stop.");
        RuleFor(command => command.Target).GreaterThan(command => command.Entry).WithErrorCode("invalid_value").WithMessage("The target is above the entry.");
    }
}