namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     Метаданные порта. Отсутствующий ValueType означает вывод типа из настроек или входов ноды.
/// </summary>
public sealed record PortSpec(
    string Key,
    PortKind Kind,
    string Label,
    ValueType? ValueType = null,
    EventPayloadType? EventPayloadType = null)
{
    public bool IsPolymorphic => ValueType is null;
}