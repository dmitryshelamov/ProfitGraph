namespace ProfitGraph.Engine.Dispatch;

/// <summary>
///     An ordered lifecycle payload awaiting its own occurrence identity.
/// </summary>
public sealed record LifecycleEmission(EventKind Kind, EventPayload Payload);