using ProfitGraph.Domain.Bots.Alerts;

namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     An alert about a bot: its kind and the status or rejection reason behind it, by name.
/// </summary>
public sealed record BotAlertResponse(Guid Id, Guid BotId, BotAlertKind Kind, string? Reason, DateTimeOffset CreatedAt, DateTimeOffset? AcknowledgedAt);