namespace ProfitGraph.Domain.Bots;

/// <summary>
///     What the owner wants the bot to do; the runtime follows it.
/// </summary>
public enum BotDesiredState
{
    Stopped,
    Running
}