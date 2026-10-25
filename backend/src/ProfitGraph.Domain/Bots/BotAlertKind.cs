namespace ProfitGraph.Domain.Bots;

/// <summary>
///     What an alert reports (Profit Fox Designer ADR 0056): a degradation, a kill or a daily-loss kill, a submit the
///     risk controls rejected, the start of a streak in which the venue could not answer, or a refused graph change.
/// </summary>
public enum BotAlertKind
{
    Degraded,
    Killed,
    RiskRejection,
    ExchangeUnavailable,

    /// <summary>
    ///     A saved version the running bot could not switch to; it keeps its version.
    /// </summary>
    GraphChangeRejected
}