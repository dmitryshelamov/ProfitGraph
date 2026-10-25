namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     Fills newest first; <see cref="Next" /> continues after the last one, or is null at the end.
/// </summary>
public sealed record BotFillPageInternal(IReadOnlyList<BotFillInternal> Items, long? Next);