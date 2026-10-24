namespace ProfitGraph.Engine.Nodes;

/// <summary>
///     One known event-field choice with its concrete value type and required payload.
/// </summary>
public sealed record EventFieldSpec(string Key, ValueType ValueType, EventPayloadType PayloadType);