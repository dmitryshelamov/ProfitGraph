namespace ProfitGraph.Application.Bots.JoinBotFormingFeed;

/// <summary>
///     The state of a forming-candle feed as clients see it.
/// </summary>
public enum FormingFeedStatus
{
    /// <summary>
    ///     The venue delivers candles.
    /// </summary>
    Live,

    /// <summary>
    ///     The venue connection is lost, being restored, or silent for too long.
    /// </summary>
    NoData,

    /// <summary>
    ///     The venue or the bot's timeframe has no forming candle; nothing will arrive.
    /// </summary>
    Unsupported
}