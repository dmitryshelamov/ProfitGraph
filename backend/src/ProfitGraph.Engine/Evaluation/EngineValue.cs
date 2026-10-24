using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Engine.Evaluation;

/// <summary>
///     Типизированное значение replay. Числа хранятся только в decimal; отсутствие отличается от нуля.
/// </summary>
public readonly struct EngineValue : IEquatable<EngineValue>
{
    private readonly decimal _number;
    private readonly bool _boolean;
    private readonly long _orderId;
    private readonly object? _payload;
    private readonly int _enumValue;

    // Отсутствующее значение запрашивается сотни раз на свечу, поэтому готовые экземпляры берутся по индексу типа.
    private static readonly EngineValue[] UnavailableValues = Enum.GetValues<ValueType>()
        .OrderBy(type => (int)type)
        .Select(type => new EngineValue(type, isAvailable: false))
        .ToArray();

    public ValueType ValueType { get; }
    public bool IsAvailable { get; }
    public decimal Number => IsAvailable && UnitAlgebra.IsNumeric(ValueType) ? _number : throw new InvalidOperationException("Value is not an available number.");
    public bool Boolean => Require(ValueType.Boolean, _boolean);
    public string Tag => (string)Require(ValueType.Tag, _payload)!;
    public long OrderId => Require(ValueType.OrderId, _orderId);
    public Candle Candle => (Candle)Require(ValueType.Candle, _payload)!;
    public OrderSnapshot Order => (OrderSnapshot)Require(ValueType.Order, _payload)!;
    public EventOccurrence Event => (EventOccurrence)Require(ValueType.Event, _payload)!;
    public OrderSide Side => (OrderSide)Require(ValueType.OrderSide, _enumValue);
    public OrderType Type => (OrderType)Require(ValueType.OrderType, _enumValue);
    public RejectionReason Reason => (RejectionReason)Require(ValueType.RejectionReason, _enumValue);

    private EngineValue(
        ValueType valueType,
        bool isAvailable,
        decimal number = default,
        bool boolean = default,
        string? tag = null,
        long orderId = default,
        Candle? candle = null,
        OrderSnapshot? order = null,
        EventOccurrence? occurrence = null,
        OrderSide side = default,
        OrderType type = default,
        RejectionReason reason = default)
    {
        ValueType = valueType;
        IsAvailable = isAvailable;
        _number = number;
        _boolean = boolean;
        _orderId = orderId;
        _payload = tag ?? (object?)candle ?? (object?)order ?? occurrence;
        _enumValue = valueType switch
        {
            ValueType.OrderSide => (int)side,
            ValueType.OrderType => (int)type,
            ValueType.RejectionReason => (int)reason,
            _ => 0
        };
    }

    public static EngineValue Unavailable(ValueType type)
    {
        EngineValue[] values = UnavailableValues;

        if ((uint)type >= (uint)values.Length || values[(int)type].ValueType != type)
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        return values[(int)type];
    }

    public static EngineValue Numeric(ValueType type, decimal value)
    {
        if (!UnitAlgebra.IsNumeric(type) || (type == ValueType.Integer && decimal.Truncate(value) != value))
        {
            throw new ArgumentException("Expected a numeric type and an integral value for Integer.", nameof(type));
        }

        return new EngineValue(type, isAvailable: true, value);
    }

    public static EngineValue FromBoolean(bool value)
    {
        return new EngineValue(ValueType.Boolean, isAvailable: true, boolean: value);
    }

    public static EngineValue FromTag(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new EngineValue(ValueType.Tag, isAvailable: true, tag: value);
    }

    public static EngineValue FromOrderId(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

        return new EngineValue(ValueType.OrderId, isAvailable: true, orderId: value);
    }

    public static EngineValue FromCandle(Candle value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new EngineValue(ValueType.Candle, isAvailable: true, candle: value);
    }

    public static EngineValue FromOrder(OrderSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new EngineValue(ValueType.Order, isAvailable: true, order: value);
    }

    public static EngineValue FromEvent(EventOccurrence value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new EngineValue(ValueType.Event, isAvailable: true, occurrence: value);
    }

    public static EngineValue FromOrderSide(OrderSide value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return new EngineValue(ValueType.OrderSide, isAvailable: true, side: value);
    }

    public static EngineValue FromOrderType(OrderType value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return new EngineValue(ValueType.OrderType, isAvailable: true, type: value);
    }

    public static EngineValue FromRejectionReason(RejectionReason value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return new EngineValue(ValueType.RejectionReason, isAvailable: true, reason: value);
    }

    public bool Equals(EngineValue other)
    {
        return ValueType == other.ValueType && IsAvailable == other.IsAvailable && _number == other._number && _boolean == other._boolean &&
            _orderId == other._orderId && _enumValue == other._enumValue && Equals(_payload, other._payload);
    }

    public override bool Equals(object? obj)
    {
        return obj is EngineValue other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ValueType, IsAvailable, _number, _boolean, _orderId, _enumValue, _payload);
    }

    private T Require<T>(ValueType type, T value)
    {
        if (!IsAvailable || ValueType != type)
        {
            throw new InvalidOperationException($"Value is not an available {type}.");
        }

        return value;
    }
}