namespace ProfitGraph.Application.Bots.ReportBotRuntime;

/// <summary>
///     What the runtime that holds a bot's lease reports about it.
/// </summary>
public enum BotRuntimeReport
{
    Starting,
    Running,
    Stopped,
    Failed,
    Degraded
}