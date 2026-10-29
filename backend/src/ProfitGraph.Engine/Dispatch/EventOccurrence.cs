using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Одно событие replay. Глобальный listener и прямой lifecycle-порт передают один экземпляр с одним ID.
/// </summary>
public sealed record EventOccurrence
{
    public long Id { get; }
    public long Sequence { get; }
    public long? CausationId { get; }
    public EventKind Kind { get; }
    public int CandleIndex { get; }
    public DateTimeOffset CandleOpenTime { get; }
    public DateTimeOffset CandleCloseTime { get; }
    public PricePhase Phase { get; }
    public decimal MarketPrice { get; }
    public EventPayload Payload { get; }

    public EventOccurrence(
        long id,
        long sequence,
        long? causationId,
        EventKind kind,
        int candleIndex,
        DateTimeOffset candleOpenTime,
        DateTimeOffset candleCloseTime,
        PricePhase phase,
        decimal marketPrice,
        EventPayload payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfNegative(candleIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(marketPrice);
        ArgumentNullException.ThrowIfNull(payload);

        if (causationId is <= 0 || causationId >= id || candleOpenTime.Offset != TimeSpan.Zero || candleCloseTime.Offset != TimeSpan.Zero ||
            candleCloseTime <= candleOpenTime || !Enum.IsDefined(phase) || !Matches(kind, payload))
        {
            throw new ArgumentException("Invalid event identity, UTC interval, phase or payload.");
        }

        Id = id;
        Sequence = sequence;
        CausationId = causationId;
        Kind = kind;
        CandleIndex = candleIndex;
        CandleOpenTime = candleOpenTime;
        CandleCloseTime = candleCloseTime;
        Phase = phase;
        MarketPrice = marketPrice;
        Payload = payload;
    }

    private static bool Matches(EventKind kind, EventPayload payload)
    {
        return (kind, payload.PayloadType) is
            (EventKind.Start, EventPayloadType.Start) or
            (EventKind.CandleClosed, EventPayloadType.Candle) or
            (EventKind.Submitted or EventKind.Cancelled, EventPayloadType.Order) or
            (EventKind.Filled, EventPayloadType.Fill) or
            (EventKind.Rejected, EventPayloadType.Rejection) or
            (EventKind.CancelCompleted, EventPayloadType.CancelResult) or
            (EventKind.Resume, EventPayloadType.Resume) or
            (EventKind.GridStarted or EventKind.GridStopped, EventPayloadType.Grid);
    }
}