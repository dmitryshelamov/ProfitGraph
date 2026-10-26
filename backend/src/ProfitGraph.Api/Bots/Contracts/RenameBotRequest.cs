namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     The bot's new name; null or blank clears it, so the bot is shown by its symbol and timeframe again.
/// </summary>
public sealed record RenameBotRequest(string? Name);