namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     Feed entries newest first; <see cref="Next" /> continues after the last one, or is null at the end.
/// </summary>
public sealed record BotFeedPageInternal(IReadOnlyList<BotFeedEntryInternal> Items, long? Next);