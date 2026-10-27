namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The stable codes of the checks that prove a missing order absent (ADR 0030): a passed check goes to the bot's feed, a
///     failed one is the reason the bot stays stopped. Codes hold no account data; the frontend words them.
/// </summary>
internal static class TInvestAbsenceChecks
{
    public const string NotFoundByRequestId = "NotFoundByRequestId";
    public const string NotAmongActiveOrders = "NotAmongActiveOrders";
    public const string NoExecutionRecorded = "NoExecutionRecorded";
    public const string NotFoundByExchangeId = "NotFoundByExchangeId";
    public const string TradingDayEnded = "TradingDayEnded";
    public const string NoExecutionInOperations = "NoExecutionInOperations";
    public const string PositionMatchesEngine = "PositionMatchesEngine";

    public const string ActiveOrderFound = "ActiveOrderFound";
    public const string ExecutionRecorded = "ExecutionRecorded";
    public const string KnownByExchangeId = "KnownByExchangeId";
    public const string TradingDayUnknown = "TradingDayUnknown";
    public const string TradingDayNotEnded = "TradingDayNotEnded";
    public const string OrderTooOld = "OrderTooOld";
    public const string OperationsIncomplete = "OperationsIncomplete";
    public const string ExecutionInOperations = "ExecutionInOperations";
    public const string ExpectedPositionUnknown = "ExpectedPositionUnknown";
    public const string PositionUnreadable = "PositionUnreadable";
    public const string PositionBlocked = "PositionBlocked";
    public const string PositionDiffers = "PositionDiffers";
    public const string ReadFailed = "ReadFailed";
    public const string AttemptChanged = "AttemptChanged";
}
