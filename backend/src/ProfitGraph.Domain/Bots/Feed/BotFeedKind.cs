namespace ProfitGraph.Domain.Bots.Feed;

/// <summary>
///     What an entry of a bot's event feed reports (ADR 0021).
/// </summary>
public enum BotFeedKind
{
    Started,
    Stopped,
    Killed,
    Degraded,
    Recovered,
    RuntimeStopped,
    RunRestarted,
    GraphVersionApplied,
    GraphChangeRejected,
    FeedStalled,
    FeedRecovered,
    ExchangeUnavailable,
    SubmitHeld,
    OrderPlaced,
    OrderRejected,
    OrderFailed,
    OrderAbsenceConfirmed,
    Fill,
    PositionOpened,
    PositionClosed,
    SettingsChanged,
    FeeCorrected,
    OrderWaiting,
    OrderWaitingEnded,
    HistoryRestated,
    OrderAbsenceProven,
    OrderCancelRequested
}