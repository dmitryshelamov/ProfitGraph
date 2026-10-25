namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The exact interval tokens of the share's trading calendar (Profit Fox Designer ADR 0090, version
///     <c>tinvest-schedule/v1</c>): specific exchange tokens, specific holiday tokens, dealer tokens that are never exchange
///     time, and generic tokens that count only when a specific token covers exactly the same interval. Nothing is matched by
///     substring except the <c>dealer_</c> prefix; any other token is unknown.
/// </summary>
internal static class TInvestScheduleVocabulary
{
    private static readonly HashSet<string> Exchange = new HashSet<string>(StringComparer.Ordinal)
    {
        "opening_auction_morning",
        "regular_trading_session_morning",
        "regular_trading_session_main",
        "regular_trading_session_evening",
        "closing_auction_main"
    };

    private static readonly HashSet<string> Holiday = new HashSet<string>(StringComparer.Ordinal)
    {
        "holiday_opening_auction_morning",
        "holiday_trading_session_morning"
    };

    private static readonly HashSet<string> Generic = new HashSet<string>(StringComparer.Ordinal)
    {
        "opening_auction",
        "regular_trading_session",
        "closing_auction"
    };

    public static bool IsExchange(string type)
    {
        return Exchange.Contains(type);
    }

    public static bool IsHoliday(string type)
    {
        return Holiday.Contains(type);
    }

    public static bool IsGeneric(string type)
    {
        return Generic.Contains(type);
    }

    public static bool IsDealer(string type)
    {
        return type.StartsWith("dealer_", StringComparison.Ordinal);
    }
}