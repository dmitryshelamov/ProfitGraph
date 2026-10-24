namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Значение одного выходного порта с явным видом Series, Condition или Event.
///     Хранит числовое или логическое значение без object-боксинга, уменьшая нагрузку на GC при частых вычислениях.
/// </summary>
/// <remarks>Поле с полезным значением определяется Kind; остальные поля не представляют результат этого порта.</remarks>
public readonly struct NodeOutput
{
    private NodeOutput(PortKind kind, double seriesValue, bool conditionValue, bool eventFired, bool isAvailable = true)
    {
        Kind = kind;
        SeriesValue = seriesValue;
        ConditionValue = conditionValue;
        EventFired = eventFired;
        IsAvailable = isAvailable;
    }

    public PortKind Kind { get; }

    public double SeriesValue { get; }

    public bool ConditionValue { get; }

    public bool EventFired { get; }

    public bool IsAvailable { get; }

    public static NodeOutput Unavailable(PortKind kind)
    {
        return new NodeOutput(kind, seriesValue: default, conditionValue: default, eventFired: default, isAvailable: false);
    }

    public static NodeOutput Series(double value)
    {
        return new NodeOutput(PortKind.Series, value, conditionValue: default, eventFired: default);
    }

    public static NodeOutput Condition(bool value)
    {
        return new NodeOutput(PortKind.Condition, seriesValue: default, value, eventFired: default);
    }

    public static NodeOutput Event(bool fired)
    {
        return new NodeOutput(PortKind.Event, seriesValue: default, conditionValue: default, fired);
    }
}