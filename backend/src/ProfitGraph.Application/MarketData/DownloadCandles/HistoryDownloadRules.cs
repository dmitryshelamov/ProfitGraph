using FluentValidation;
using FluentValidation.Results;

using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData.DownloadCandles;

/// <summary>
///     Checks of a download request that need no clock and no I/O: used when the download is queued and again when the
///     Worker runs it.
/// </summary>
internal static class HistoryDownloadRules
{
    internal static void ValidDownload<T>(
        this AbstractValidator<T> validator,
        VenueRegistry venues,
        VenuePorts<IHistoricalCandleSource> sources,
        Func<T, string?> venue,
        Func<T, string?> symbol,
        Func<T, string?> timeframe,
        Func<T, DateTimeOffset?> from,
        Func<T, DateTimeOffset?> to)
    {
        validator.ValidSeries(venues, venue, symbol, timeframe);
        validator.RuleFor(request => request)
            .Custom((request, context) =>
            {
                if (venues.TryResolveSeries(venue(request), symbol(request), timeframe(request), out CandleSeries? series).Count > 0)
                {
                    return;
                }

                if (sources.Find(series!.Venue) is null)
                {
                    context.AddFailure(
                        new ValidationFailure("Venue", "This venue offers no public history download.")
                            { ErrorCode = HistoricalDataErrorCodes.Unavailable });
                }

                DateTimeOffset? start = from(request);
                DateTimeOffset? end = to(request);

                foreach ((string field, DateTimeOffset? boundary) in new[] { ("From", start), ("To", end) })
                {
                    if (boundary is { } value && (value.Offset != TimeSpan.Zero || value < DateTimeOffset.UnixEpoch || !series.Timeframe.IsAligned(value)))
                    {
                        context.AddFailure(field, "Use a UTC time on the timeframe grid, at or after 1970-01-01.");
                    }
                }

                if (start is not null && end is not null && end <= start)
                {
                    context.AddFailure("To", "The end must come after the start.");
                }
            });
    }
}