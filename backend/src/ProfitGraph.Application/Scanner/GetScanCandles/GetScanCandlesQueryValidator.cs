using FluentValidation;

using ProfitGraph.Application.MarketData;

namespace ProfitGraph.Application.Scanner.GetScanCandles;

internal sealed class GetScanCandlesQueryValidator : AbstractValidator<GetScanCandlesQuery>
{
    public GetScanCandlesQueryValidator(VenueRegistry venues)
    {
        this.ValidSeries(venues, query => query.Venue, query => query.Symbol, query => query.Timeframe);
        RuleFor(query => query.From).Must(value => value.Offset == TimeSpan.Zero).WithErrorCode("invalid_value").WithMessage("Use a UTC time.");
        RuleFor(query => query.To)
            .Must(value => value.Offset == TimeSpan.Zero)
            .WithErrorCode("invalid_value")
            .WithMessage("Use a UTC time.")
            .GreaterThan(query => query.From)
            .WithErrorCode("invalid_value")
            .WithMessage("The end must come after the start.");
    }
}