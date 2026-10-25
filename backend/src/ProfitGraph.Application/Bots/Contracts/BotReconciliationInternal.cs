using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     The venue's answer for an opening bot: engine updates for the orders it found, the orders themselves, and a safety
///     reason when the bot must not trade (an unresolved order, a foreign order, wrong key permissions).
/// </summary>
public sealed record BotReconciliationInternal(
    IReadOnlyList<ExternalOrderUpdate> Updates,
    IReadOnlyList<ReconciledOrderInternal> Found,
    BotStatusReason? Degradation);