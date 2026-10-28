namespace ProfitGraph.Application.Notifications.Contracts;

/// <summary>
///     What one claim took: how many notifications it claimed, and the messages among them to send now.
/// </summary>
public sealed record TelegramClaimInternal(int Claimed, IReadOnlyList<TelegramOutgoingInternal> Messages);