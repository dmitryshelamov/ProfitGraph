namespace ProfitGraph.Domain.Bots;

/// <summary>
///     What the runtime last reported for the bot.
/// </summary>
public enum BotRuntimeStatus
{
    Created,
    Starting,
    Running,
    Degraded,
    Stopped
}