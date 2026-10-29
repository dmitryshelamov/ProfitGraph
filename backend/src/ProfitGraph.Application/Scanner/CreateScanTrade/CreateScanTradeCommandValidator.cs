using FluentValidation;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Scanner.CreateScanTrade;

internal sealed class CreateScanTradeCommandValidator : AbstractValidator<CreateScanTradeCommand>
{
    public CreateScanTradeCommandValidator(VenueRegistry venues)
    {
        this.ValidSeries(venues, command => command.Venue, command => command.Symbol, command => command.Timeframe);
        RuleFor(command => command.Scenario).IsInEnum().WithErrorCode("invalid_value").WithMessage("Unknown scenario.");
        RuleFor(command => command.Direction).IsInEnum().WithErrorCode("invalid_value").WithMessage("Unknown direction.");

        // A short trade (ADR 0061) sells at its entry: its target is below it and its stop above.
        When(
            command => command.Direction == PositionDirection.Long,
            () =>
            {
                RuleFor(command => command.Stop).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The stop is positive.");
                RuleFor(command => command.Entry).GreaterThan(command => command.Stop).WithErrorCode("invalid_value").WithMessage("The entry is above the stop.");
                RuleFor(command => command.Target).GreaterThan(command => command.Entry).WithErrorCode("invalid_value").WithMessage("The target is above the entry.");
            });
        When(
            command => command.Direction == PositionDirection.Short,
            () =>
            {
                RuleFor(command => command.Target).GreaterThan(0m).WithErrorCode("invalid_value").WithMessage("The target is positive.");
                RuleFor(command => command.Entry).GreaterThan(command => command.Target).WithErrorCode("invalid_value").WithMessage("The entry is above the target.");
                RuleFor(command => command.Stop).GreaterThan(command => command.Entry).WithErrorCode("invalid_value").WithMessage("The stop is above the entry.");
            });
    }
}