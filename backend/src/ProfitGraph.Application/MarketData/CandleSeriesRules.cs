using FluentValidation;
using FluentValidation.Results;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData;

internal static class CandleSeriesRules
{
    /// <summary>
    ///     Adds the venue, symbol and timeframe checks of <see cref="VenueRegistry.TryResolveSeries" /> to a validator.
    /// </summary>
    internal static void ValidSeries<T>(
        this AbstractValidator<T> validator,
        VenueRegistry venues,
        Func<T, string?> venue,
        Func<T, string?> symbol,
        Func<T, string?> timeframe)
    {
        validator.RuleFor(request => request)
            .Custom((request, context) =>
            {
                foreach (InputError error in venues.TryResolveSeries(venue(request), symbol(request), timeframe(request), out CandleSeries? _))
                {
                    context.AddFailure(new ValidationFailure(error.Field, error.Message) { ErrorCode = error.Code });
                }
            });
    }
}