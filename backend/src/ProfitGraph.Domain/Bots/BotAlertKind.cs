namespace ProfitGraph.Domain.Bots;

/// <summary>
///     What an alert reports (Profit Fox Designer ADR 0056): a degradation, a kill or a daily-loss kill, a submit the
///     risk controls rejected, or the start of a streak in which the venue could not answer.
/// </summary>
public enum BotAlertKind
{
    Degraded,
    Killed,
    RiskRejection,
    ExchangeUnavailable
}