using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Plain-data copy of an occurrence retained by a pending cancellation.
/// </summary>
public sealed record EventCauseCheckpoint(
    long Id,
    long Sequence,
    long? CausationId,
    EventKind Kind,
    int CandleIndex,
    DateTimeOffset CandleOpenTime,
    DateTimeOffset CandleCloseTime,
    PricePhase Phase,
    decimal MarketPrice,
    Candle? Candle,
    OrderSnapshot? Order,
    Fill? Fill,
    OrderRejection? Rejection,
    long? AttemptId,
    bool? Cancelled,
    long? CancelledOrderId,
    string? OriginNodeId,
    EventCauseCheckpoint? NestedCause,
    ResumeReason? ResumeReason,
    string? PreviousGraphVersionId,
    string? CurrentGraphVersionId,
    decimal? GridAnchor = null)
{
    public static EventCauseCheckpoint Capture(EventOccurrence occurrence)
    {
        return new EventCauseCheckpoint(
            occurrence.Id,
            occurrence.Sequence,
            occurrence.CausationId,
            occurrence.Kind,
            occurrence.CandleIndex,
            occurrence.CandleOpenTime,
            occurrence.CandleCloseTime,
            occurrence.Phase,
            occurrence.MarketPrice,
            (occurrence.Payload as CandlePayload)?.Candle,
            (occurrence.Payload as OrderPayload)?.Order,
            (occurrence.Payload as FillPayload)?.Fill,
            (occurrence.Payload as RejectionPayload)?.Rejection,
            (occurrence.Payload as RejectionPayload)?.AttemptId,
            (occurrence.Payload as CancelResultPayload)?.Cancelled,
            (occurrence.Payload as CancelResultPayload)?.CancelledOrderId,
            occurrence.Payload switch
            {
                FillPayload fill => fill.OriginNodeId,
                CancelResultPayload cancel => cancel.OriginNodeId,
                GridPayload grid => grid.OriginNodeId,
                _ => null
            },
            occurrence.Payload is CancelResultPayload result ? Capture(result.Cause) : null,
            (occurrence.Payload as ResumePayload)?.Reason,
            (occurrence.Payload as ResumePayload)?.PreviousGraphVersionId,
            (occurrence.Payload as ResumePayload)?.CurrentGraphVersionId,
            (occurrence.Payload as GridPayload)?.Anchor);
    }

    public EventOccurrence Restore()
    {
        EventPayload payload = Kind switch
        {
            EventKind.Start => new StartPayload(),
            EventKind.CandleClosed => new CandlePayload(Candle!),
            EventKind.Submitted or EventKind.Cancelled => new OrderPayload(Order!),
            EventKind.Filled => new FillPayload(Fill!, OriginNodeId!),
            EventKind.Rejected => new RejectionPayload(AttemptId!.Value, Rejection!),
            EventKind.CancelCompleted => new CancelResultPayload(Cancelled!.Value, CancelledOrderId, OriginNodeId!, NestedCause!.Restore()),
            EventKind.Resume => new ResumePayload(ResumeReason!.Value, PreviousGraphVersionId!, CurrentGraphVersionId!),
            EventKind.GridStarted or EventKind.GridStopped => new GridPayload(OriginNodeId!, GridAnchor),
            _ => throw new ArgumentException("Invalid checkpoint event kind.")
        };

        return new EventOccurrence(Id, Sequence, CausationId, Kind, CandleIndex, CandleOpenTime, CandleCloseTime, Phase, MarketPrice, payload);
    }
}