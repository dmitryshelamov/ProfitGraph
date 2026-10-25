namespace ProfitGraph.Domain.Bots;

/// <summary>
///     A fill with its position in the bot's record, the cursor of the next page.
/// </summary>
public sealed record SequencedBotFill(long Sequence, BotFill Fill);