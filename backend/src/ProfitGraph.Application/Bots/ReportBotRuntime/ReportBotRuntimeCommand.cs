using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.ReportBotRuntime;

/// <summary>
///     The lease holder reports the bot's runtime status; a report from a stale holder changes nothing and returns false.
///     <see cref="Reason" /> is the safety reason of <see cref="BotRuntimeReport.Degraded" />. <see cref="Detail" /> is a short
///     diagnostic text of a failure or a degradation that is safe to show to the owner (ADR 0029).
/// </summary>
public sealed record ReportBotRuntimeCommand(Guid BotId, Guid HolderId, long Epoch, BotRuntimeReport Report, BotStatusReason? Reason = null, string? Detail = null)
    : ICommand<bool>;