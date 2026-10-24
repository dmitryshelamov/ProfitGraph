using FluentValidation;

namespace ProfitGraph.Application.MarketData.GetCandles;

internal sealed class GetCandlesQueryValidator : AbstractValidator<GetCandlesQuery>
{
    private const int MaxLimit = 10_000;

    public GetCandlesQueryValidator(VenueRegistry venues)
    {
        this.ValidSeries(venues, query => query.Venue, query => query.Symbol, query => query.Timeframe);
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, MaxLimit);
        RuleFor(query => query.From).Must(value => value.Offset == TimeSpan.Zero).WithMessage("Use a UTC time.");
        RuleFor(query => query.To)
            .Must(value => value.Offset == TimeSpan.Zero)
            .WithMessage("Use a UTC time.")
            .GreaterThan(query => query.From);
    }
}