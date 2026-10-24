namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Conditions shown next to the bot's status that do not change it.
/// </summary>
[Flags]
public enum BotDiagnostics
{
    None = 0,

    /// <summary>
    ///     The live candle feed of the bot's series stopped delivering closed candles.
    /// </summary>
    StaleFeed = 1,

    /// <summary>
    ///     The bot was stopped with submit intents still waiting; they are held until it starts again.
    /// </summary>
    HeldSubmit = 2,

    /// <summary>
    ///     The venue is temporarily unavailable; the bot keeps running with its submit gate closed.
    /// </summary>
    ExchangeUnavailable = 4,

    /// <summary>
    ///     The latest saved strategy version could not be applied; the bot runs its previous version.
    /// </summary>
    GraphChangeRejected = 8
}