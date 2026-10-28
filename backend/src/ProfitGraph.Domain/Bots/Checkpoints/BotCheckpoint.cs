using ProfitGraph.Engine.Dispatch;

namespace ProfitGraph.Domain.Bots.Checkpoints;

/// <summary>
///     The bot's engine state after its last committed root, with the parameter values that engine was built from.
/// </summary>
public sealed record BotCheckpoint(BotId BotId, GraphEngineCheckpoint Engine, BotParameters AppliedParameters, DateTimeOffset UpdatedAt)
{
    public GraphEngineCheckpoint Engine { get; } = Engine ?? throw new ArgumentNullException(nameof(Engine));
    public BotParameters AppliedParameters { get; } = AppliedParameters ?? throw new ArgumentNullException(nameof(AppliedParameters));
}