using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Closed event-field schema; extraction never guesses monetary units from runtime payloads.
/// </summary>
public static class EventFieldCatalog
{
    public static IReadOnlyList<EventFieldSpec> Fields { get; } = Array.AsReadOnly(
        new[]
        {
            new EventFieldSpec("Candle.Open", ValueType.Price, EventPayloadType.Candle),
            new EventFieldSpec("Candle.High", ValueType.Price, EventPayloadType.Candle),
            new EventFieldSpec("Candle.Low", ValueType.Price, EventPayloadType.Candle),
            new EventFieldSpec("Candle.Close", ValueType.Price, EventPayloadType.Candle),
            new EventFieldSpec("Candle.Volume", ValueType.BaseAmount, EventPayloadType.Candle),
            new EventFieldSpec("Fill.Price", ValueType.Price, EventPayloadType.Fill),
            new EventFieldSpec("Fill.Tag", ValueType.Tag, EventPayloadType.Fill),
            new EventFieldSpec("Fill.Side", ValueType.OrderSide, EventPayloadType.Fill),
            new EventFieldSpec("Fill.Type", ValueType.OrderType, EventPayloadType.Fill),
            new EventFieldSpec("Fill.OrderId", ValueType.OrderId, EventPayloadType.Fill),
            new EventFieldSpec("Fill.GrossBase", ValueType.BaseAmount, EventPayloadType.Fill),
            new EventFieldSpec("Fill.BaseChange", ValueType.BaseAmount, EventPayloadType.Fill),
            new EventFieldSpec("Fill.QuoteChange", ValueType.QuoteAmount, EventPayloadType.Fill),
            new EventFieldSpec("Fill.FeeBase", ValueType.BaseAmount, EventPayloadType.Fill),
            new EventFieldSpec("Fill.FeeQuote", ValueType.QuoteAmount, EventPayloadType.Fill),
            new EventFieldSpec("Fill.RealizedPnl", ValueType.QuoteAmount, EventPayloadType.Fill),
            new EventFieldSpec("Order.Id", ValueType.OrderId, EventPayloadType.Order),
            new EventFieldSpec("Order.Tag", ValueType.Tag, EventPayloadType.Order),
            new EventFieldSpec("Order.Side", ValueType.OrderSide, EventPayloadType.Order),
            new EventFieldSpec("Order.Type", ValueType.OrderType, EventPayloadType.Order),
            new EventFieldSpec("Order.Price", ValueType.Price, EventPayloadType.Order),
            new EventFieldSpec("Order.Quantity", ValueType.BaseAmount, EventPayloadType.Order),
            new EventFieldSpec("Order.ReservedQuote", ValueType.QuoteAmount, EventPayloadType.Order),
            new EventFieldSpec("Order.ReservedBase", ValueType.BaseAmount, EventPayloadType.Order),
            new EventFieldSpec("Rejection.Reason", ValueType.RejectionReason, EventPayloadType.Rejection),
            new EventFieldSpec("Rejection.Side", ValueType.OrderSide, EventPayloadType.Rejection),
            new EventFieldSpec("Rejection.Type", ValueType.OrderType, EventPayloadType.Rejection),
            new EventFieldSpec("Rejection.Tag", ValueType.Tag, EventPayloadType.Rejection),
            new EventFieldSpec("Rejection.OrderId", ValueType.OrderId, EventPayloadType.Rejection),
            new EventFieldSpec("Rejection.AttemptId", ValueType.Integer, EventPayloadType.Rejection),
            new EventFieldSpec("CancelResult.Cancelled", ValueType.Boolean, EventPayloadType.CancelResult),
            new EventFieldSpec("CancelResult.OrderId", ValueType.OrderId, EventPayloadType.CancelResult)
        });

    public static EventFieldSpec? Find(string key)
    {
        return Fields.SingleOrDefault(field => field.Key == key);
    }

    public static EngineValue Extract(EventFieldSpec field, EventOccurrence occurrence)
    {
        if (occurrence.Payload.PayloadType != field.PayloadType)
        {
            throw new InvalidOperationException("The occurrence payload does not match the compiled event field.");
        }

        EngineValue Tag(string? value) => string.IsNullOrWhiteSpace(value) ? EngineValue.Unavailable(ValueType.Tag) : EngineValue.FromTag(value);

        EngineValue Number(decimal? value) => value.HasValue ? EngineValue.Numeric(field.ValueType, value.Value) : EngineValue.Unavailable(field.ValueType);

        EngineValue Id(long? value) => value.HasValue ? EngineValue.FromOrderId(value.Value) : EngineValue.Unavailable(ValueType.OrderId);

        return occurrence.Payload switch
        {
            CandlePayload payload => Number(
                field.Key switch
                {
                    "Candle.Open" => payload.Candle.Open, "Candle.High" => payload.Candle.High, "Candle.Low" => payload.Candle.Low,
                    "Candle.Close" => payload.Candle.Close, "Candle.Volume" => payload.Candle.Volume, _ => throw new InvalidOperationException("Unknown candle field.")
                }),
            FillPayload payload => field.Key switch
            {
                "Fill.Price" => Number(payload.Fill.Price), "Fill.Tag" => Tag(payload.Fill.Tag), "Fill.Side" => EngineValue.FromOrderSide(payload.Fill.Side),
                "Fill.Type" => EngineValue.FromOrderType(payload.Fill.Type), "Fill.OrderId" => Id(payload.Fill.OrderId),
                "Fill.GrossBase" => Number(payload.Fill.GrossBase),
                "Fill.BaseChange" => Number(payload.Fill.BaseChange), "Fill.QuoteChange" => Number(payload.Fill.QuoteChange),
                "Fill.FeeBase" => Number(payload.Fill.FeeAsset == FeeAsset.Base ? payload.Fill.Fee : null),
                "Fill.FeeQuote" => Number(payload.Fill.FeeAsset == FeeAsset.Quote ? payload.Fill.Fee : null),
                "Fill.RealizedPnl" => Number(payload.Fill.RealizedPnl), _ => throw new InvalidOperationException("Unknown fill field.")
            },
            OrderPayload payload => field.Key switch
            {
                "Order.Id" => Id(payload.Order.Id), "Order.Tag" => Tag(payload.Order.Tag), "Order.Side" => EngineValue.FromOrderSide(payload.Order.Side),
                "Order.Type" => EngineValue.FromOrderType(payload.Order.Type), "Order.Price" => Number(payload.Order.LimitPrice),
                "Order.Quantity" => Number(payload.Order.Quantity),
                "Order.ReservedQuote" => Number(payload.Order.Side == OrderSide.Buy ? payload.Order.Reservation : null),
                "Order.ReservedBase" => Number(payload.Order.Side == OrderSide.Sell ? payload.Order.Reservation : null),
                _ => throw new InvalidOperationException("Unknown order field.")
            },
            RejectionPayload payload => field.Key switch
            {
                "Rejection.Reason" => EngineValue.FromRejectionReason(payload.Rejection.Reason), "Rejection.Side" => EngineValue.FromOrderSide(payload.Rejection.Side),
                "Rejection.Type" => EngineValue.FromOrderType(payload.Rejection.Type), "Rejection.Tag" => Tag(payload.Rejection.Tag),
                "Rejection.OrderId" => Id(payload.Rejection.OrderId), "Rejection.AttemptId" => Number(payload.AttemptId),
                _ => throw new InvalidOperationException("Unknown rejection field.")
            },
            CancelResultPayload payload => field.Key switch
            {
                "CancelResult.Cancelled" => EngineValue.FromBoolean(payload.Cancelled), "CancelResult.OrderId" => Id(payload.CancelledOrderId),
                _ => throw new InvalidOperationException("Unknown cancel-result field.")
            },
            _ => throw new InvalidOperationException("Unsupported event payload.")
        };
    }
}