namespace ProfitGraph.Domain.Bots.PositionCloses;

/// <summary>
///     Where an owner's position close is (ADR 0064): waiting for the runner, cancelling the bot's orders, selling the position,
///     or ended.
/// </summary>
public enum BotPositionCloseStatus
{
    Pending,
    Cancelling,
    Selling,
    Closed,
    Failed
}