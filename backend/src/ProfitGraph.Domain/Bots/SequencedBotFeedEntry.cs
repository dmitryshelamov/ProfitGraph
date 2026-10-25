namespace ProfitGraph.Domain.Bots;

/// <summary>
///     A feed entry with its position in the bot's feed, the cursor of the next page.
/// </summary>
public sealed record SequencedBotFeedEntry(long Sequence, BotFeedEntry Entry);