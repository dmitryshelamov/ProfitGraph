namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     How a card followed by the bot's rules ended (ADR 0071, section 2): whether its entry filled within its wait, and for a filled
///     one how it left, its result in R after costs (the quote it made over the quote its stop risks), when it entered and left and the
///     price it left at (ADR 0073), empty in an outcome stored before it.
/// </summary>
public sealed record ScanCardOutcome(
    bool Filled,
    ScanTradeExit? Exit,
    decimal? ResultR,
    DateTimeOffset? EnteredAt,
    DateTimeOffset? ExitedAt,
    decimal? ExitPrice = null)
{
    public static ScanCardOutcome Unfilled { get; } = new ScanCardOutcome(Filled: false, Exit: null, ResultR: null, EnteredAt: null, ExitedAt: null);
}