using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Jobs.Contracts;
using ProfitGraph.Application.MarketData.DownloadCandles;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Application.MarketData.StartCandleDownload;

/// <summary>
///     Rejects a start at or after the latest closed candle at once, so the user gets 400 instead of a failed job; the
///     Worker repeats the check with its own clock reading. The queued request carries the canonical series.
/// </summary>
internal sealed class StartCandleDownloadCommandHandler(VenueRegistry venues, BackgroundJobQueue queue, TimeProvider clock)
    : IRequestHandler<StartCandleDownloadCommand, BackgroundJobInternal>
{
    public Task<BackgroundJobInternal> Handle(StartCandleDownloadCommand request, CancellationToken cancellationToken)
    {
        CandleSeries series = venues.ResolveSeries(request.Venue, request.Symbol, request.Timeframe);

        if (request.From >= series.Timeframe.FloorToGrid(clock.GetUtcNow()))
        {
            throw new ApplicationValidationException([new InputError("invalid_request", "From", "The start must come before the latest closed candle.")]);
        }

        var download = new DownloadCandlesCommand(series.Venue.Value, series.Symbol.Value, series.Timeframe.Key, request.From, request.To);

        return queue.Enqueue(new UserId(request.UserId), download, cancellationToken);
    }
}