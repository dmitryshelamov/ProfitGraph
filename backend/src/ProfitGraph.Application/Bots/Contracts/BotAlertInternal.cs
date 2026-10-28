using ProfitGraph.Domain.Bots.Alerts;

namespace ProfitGraph.Application.Bots.Contracts;

public sealed record BotAlertInternal(Guid Id, Guid BotId, BotAlertKind Kind, string? Reason, DateTimeOffset CreatedAt, DateTimeOffset? AcknowledgedAt);