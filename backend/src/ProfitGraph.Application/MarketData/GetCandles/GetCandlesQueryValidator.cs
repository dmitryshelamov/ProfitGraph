using FluentValidation;

using ProfitGraph.Application.Common.Queries;

namespace ProfitGraph.Application.MarketData.GetCandles;

internal sealed class GetCandlesQueryValidator : AbstractValidator<GetCandlesQuery>
{
    public GetCandlesQueryValidator(VenueRegistry venues, QueryLimitOptions limits)
    {
        this.ValidSeries(venues, query => query.Venue, query => query.Symbol, query => query.Timeframe);
        RuleFor(query => query.Limit).InclusiveBetween(from: 1, limits.Candles);
        RuleFor(query => query.From).Must(value => value.Offset == TimeSpan.Zero).WithMessage("Use a UTC time.");
        RuleFor(query => query.To)
            .Must(value => value.Offset == TimeSpan.Zero)
            .WithMessage("Use a UTC time.")
            .GreaterThan(query => query.From);
    }
}