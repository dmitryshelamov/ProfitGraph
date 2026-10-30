namespace ProfitGraph.Domain.Bots.Fills;

/// <summary>
///     The fills made from <see cref="From" /> through <see cref="To" />, both included.
/// </summary>
public sealed record BotFillRange(DateTimeOffset From, DateTimeOffset To);