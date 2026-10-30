using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots.PositionCloses;

/// <summary>
///     A position close ended as <see cref="BotPositionCloseStatus.Closed" /> or <see cref="BotPositionCloseStatus.Failed" /> with
///     its reason code.
/// </summary>
public sealed record BotPositionCloseEnded(BotId BotId, BotPositionCloseId CloseId, BotPositionCloseStatus Status, string? Reason, DateTimeOffset At)
    : IDomainEvent;