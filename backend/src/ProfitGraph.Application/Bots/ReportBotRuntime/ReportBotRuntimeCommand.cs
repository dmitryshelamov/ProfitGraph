using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.ReportBotRuntime;

/// <summary>
///     The lease holder reports the bot's runtime status; a report from a stale holder changes nothing and returns false.
///     <see cref="Reason" /> is the safety reason of <see cref="BotRuntimeReport.Degraded" />.
/// </summary>
public sealed record ReportBotRuntimeCommand(Guid BotId, Guid HolderId, long Epoch, BotRuntimeReport Report, BotStatusReason? Reason = null) : ICommand<bool>;