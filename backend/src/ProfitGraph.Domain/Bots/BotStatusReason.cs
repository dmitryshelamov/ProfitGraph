namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Why a bot is degraded or was stopped by a control. User-facing texts are produced from these codes at the
///     presentation and notification boundaries (ADR 0004).
/// </summary>
public enum BotStatusReason
{
    /// <summary>
    ///     A runtime failure; the bot restarts automatically with backoff.
    /// </summary>
    RuntimeFailure,

    /// <summary>
    ///     Trading is disabled in this process, so no order gateway exists.
    /// </summary>
    NoGateway,

    UnknownVenue,
    IntentFailed,
    UnresolvedOrder,
    ForeignOrder,
    KeyPermissions,
    UnsupportedFeeAsset,
    InsufficientBalance,
    HistoryDataCorrection,

    /// <summary>
    ///     The venue account's protection stopped every bot of the credential (ADR 0022, C7); resuming the protection
    ///     re-arms them.
    /// </summary>
    VenueAccountProtection,

    /// <summary>
    ///     A per-bot or global kill switch stopped the bot.
    /// </summary>
    Killed,

    /// <summary>
    ///     The daily loss limit stopped the bot.
    /// </summary>
    DailyLossLimit,

    /// <summary>
    ///     The venue holds the bot's symbol in a position mode the bot cannot trade, such as a futures symbol that nets long and
    ///     short in one position (ADR 0061); the owner switches it and resumes the bot.
    /// </summary>
    VenuePositionMode
}