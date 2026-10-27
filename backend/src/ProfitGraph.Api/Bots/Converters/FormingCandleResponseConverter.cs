using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.JoinBotFormingFeed;

namespace ProfitGraph.Api.Bots.Converters;

internal static class FormingCandleResponseConverter
{
    public static FormingCandleResponse ToResponse(this FormingCandleInternal candle, FormingFeedKeyInternal key)
    {
        return new FormingCandleResponse(
            key.Venue,
            key.Symbol,
            key.Timeframe,
            candle.OpenTime,
            ExactGraphNumberParser.Format(candle.Open),
            ExactGraphNumberParser.Format(candle.High),
            ExactGraphNumberParser.Format(candle.Low),
            ExactGraphNumberParser.Format(candle.Close),
            ExactGraphNumberParser.Format(candle.Volume),
            candle.IsClosed,
            candle.ObservedAt);
    }

    public static FormingStatusResponse ToResponse(this FormingFeedStatus status, FormingFeedKeyInternal key)
    {
        return new FormingStatusResponse(key.Venue, key.Symbol, key.Timeframe, status.ToString());
    }

    public static FormingFeedSnapshotResponse ToResponse(this FormingFeedSnapshotInternal snapshot)
    {
        return new FormingFeedSnapshotResponse(
            snapshot.Key.Venue,
            snapshot.Key.Symbol,
            snapshot.Key.Timeframe,
            snapshot.Status.ToString(),
            snapshot.Candle?.ToResponse(snapshot.Key));
    }
}