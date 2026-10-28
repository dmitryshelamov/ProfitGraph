using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Notifications.Contracts;

namespace ProfitGraph.Application.Notifications.ClaimTelegramNotifications;

/// <summary>
///     Claims up to <paramref name="Limit" /> due notifications for the sender <paramref name="ClaimId" /> (ADR 0042) and
///     returns those to send with their texts. A notification too old to send is skipped, and one whose owner has no
///     usable chat any more finishes as chat unavailable, without a request.
/// </summary>
public sealed record ClaimTelegramNotificationsCommand(Guid ClaimId, int Limit) : ICommand<TelegramClaimInternal>;