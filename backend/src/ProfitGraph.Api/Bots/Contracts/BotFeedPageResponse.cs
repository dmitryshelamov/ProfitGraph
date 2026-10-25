namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     Feed entries newest first; pass <see cref="Next" /> as <c>before</c> for the next page.
/// </summary>
public sealed record BotFeedPageResponse(IReadOnlyList<BotFeedEntryResponse> Items, long? Next);