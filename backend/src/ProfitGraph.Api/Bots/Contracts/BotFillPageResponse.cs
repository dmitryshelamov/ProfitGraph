namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     Fills newest first; pass <see cref="Next" /> as <c>before</c> for the next page.
/// </summary>
public sealed record BotFillPageResponse(IReadOnlyList<BotFillResponse> Items, long? Next);