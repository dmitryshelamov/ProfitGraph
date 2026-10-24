using ProfitGraph.Engine.Nodes;

namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     Неизменяемая типизированная полезная нагрузка события симуляции.
/// </summary>
public abstract record EventPayload
{
    public abstract EventPayloadType PayloadType { get; }
}