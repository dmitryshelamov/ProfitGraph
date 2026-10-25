using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.AcknowledgeBotAlert;

/// <summary>
///     Marks an alert of the user's bot read; another user's alert is reported as missing.
/// </summary>
public sealed record AcknowledgeBotAlertCommand(Guid UserId, Guid Id) : ICommand<BotAlertInternal>;