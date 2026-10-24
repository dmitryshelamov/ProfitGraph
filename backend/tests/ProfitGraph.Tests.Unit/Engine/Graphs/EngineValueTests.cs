using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class EngineValueTests
{
    [Test]
    public void DecimalPrecisionAndUnavailableArePreserved()
    {
        const decimal number = 123456789.123456789123456789m;
        EngineValue value = EngineValue.Numeric(ValueType.QuoteAmount, number);

        value.Number.ShouldBe(number);
        value.ValueType.ShouldBe(ValueType.QuoteAmount);
        EngineValue.Unavailable(ValueType.Price).IsAvailable.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => EngineValue.Unavailable(ValueType.Price).Number);
        Should.Throw<InvalidOperationException>(() => value.Boolean);
        EngineValue.FromBoolean(false).IsAvailable.ShouldBeTrue();
        EngineValue.FromBoolean(false).Boolean.ShouldBeFalse();
    }

    [Test]
    public void UnavailableKeepsEveryTypeAndRejectsUnknownOnes()
    {
        foreach (ValueType type in Enum.GetValues<ValueType>())
        {
            EngineValue value = EngineValue.Unavailable(type);
            value.ValueType.ShouldBe(type);
            value.IsAvailable.ShouldBeFalse();
        }

        Should.Throw<ArgumentOutOfRangeException>(() => EngineValue.Unavailable((ValueType)(-1)));
        Should.Throw<ArgumentOutOfRangeException>(() => EngineValue.Unavailable((ValueType)Enum.GetValues<ValueType>().Length));
    }

    [Test]
    public void NumericFactoriesRejectWrongTypeAndFractionalCounts()
    {
        Should.Throw<ArgumentException>(() => EngineValue.Numeric(ValueType.Tag, value: 1m));
        Should.Throw<ArgumentException>(() => EngineValue.Numeric(ValueType.Integer, value: 1.5m));
        Should.Throw<ArgumentOutOfRangeException>(() => EngineValue.FromOrderId(0));
        EngineValue.FromOrderId(1).OrderId.ShouldBe(1);
        EngineValue.FromTag("entry").Tag.ShouldBe("entry");
    }

    [Test]
    public void SequencesAndCausationAreDeterministicAndPayloadsRemainTyped()
    {
        var first = new RunSequence();
        var second = new RunSequence();
        first.Next().ShouldBe(second.Next());
        EventOccurrence start = Occurrence(first.Next(), EventKind.Start, new StartPayload(), causationId: 1);
        EngineValue.FromEvent(start).Event.ShouldBeSameAs(start);
        EventOccurrence cancellation = Occurrence(
            first.Next(),
            EventKind.CancelCompleted,
            new CancelResultPayload(Cancelled: false, CancelledOrderId: null, "cancel", start),
            start.Id);

        cancellation.CausationId.ShouldBe(start.Id);
        cancellation.Payload.ShouldBeOfType<CancelResultPayload>().Cause.ShouldBeSameAs(start);
        ((CancelResultPayload)cancellation.Payload).Cancelled.ShouldBeFalse();
    }

    [Test]
    public void InvalidEventIdentityPhaseAndPayloadAreRejected()
    {
        Should.Throw<ArgumentException>(() => Occurrence(id: 2, EventKind.Start, new StartPayload(), causationId: 2));
        Should.Throw<ArgumentException>(() => Occurrence(id: 2, EventKind.Filled, new StartPayload()));
        var candle = new Candle(DateTimeOffset.UnixEpoch, Open: 1m, High: 1m, Low: 1m, Close: 1m, Volume: 0m);
        EventOccurrence closed = Occurrence(id: 2, EventKind.CandleClosed, new CandlePayload(candle));
        EngineValue.FromCandle(candle).Candle.ShouldBeSameAs(candle);
        closed.Payload.ShouldBeOfType<CandlePayload>().Candle.ShouldBeSameAs(candle);
        Should.Throw<ArgumentException>(() => new EventOccurrence(
            id: 2,
            sequence: 2,
            causationId: null,
            EventKind.Start,
            candleIndex: 0,
            DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)),
            DateTimeOffset.UnixEpoch.AddHours(1),
            PricePhase.Open,
            marketPrice: 1m,
            new StartPayload()));
    }

    private static EventOccurrence Occurrence(long id, EventKind kind, EventPayload payload, long? causationId = null)
    {
        return new EventOccurrence(
            id,
            id,
            causationId,
            kind,
            candleIndex: 0,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddHours(1),
            PricePhase.Open,
            marketPrice: 1m,
            payload);
    }
}