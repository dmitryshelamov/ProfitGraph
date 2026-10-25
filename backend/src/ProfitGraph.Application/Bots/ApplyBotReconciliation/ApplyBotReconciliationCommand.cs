using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.ApplyBotReconciliation;

/// <summary>
///     Records a reconciliation under the holder's lease: intents of orders the venue reported need no further delivery,
///     unsent failed submits are tried again, the venue is available again and a safety reason degrades the bot. Returns
///     false when the lease was lost.
/// </summary>
public sealed record ApplyBotReconciliationCommand(
    Guid BotId,
    Guid HolderId,
    long Epoch,
    IReadOnlyList<string> FoundClientOrderIds,
    IReadOnlyList<string> FinalClientOrderIds,
    BotStatusReason? Degradation) : ICommand<bool>;