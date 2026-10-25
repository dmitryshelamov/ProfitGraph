namespace ProfitGraph.Application.Bots;

/// <summary>
///     A root that opened or closed the bot's position: when, and the engine's realized profit then.
/// </summary>
public sealed record BotRootPosition(BotPositionChange Change, DateTimeOffset At, decimal RealizedPnl);