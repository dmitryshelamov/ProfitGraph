using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Notifications.Contracts;

namespace ProfitGraph.Application.Notifications.CreateTelegramLinkCode;

/// <summary>
///     Issues a one-time code the user sends to the bot as <c>/start CODE</c>; it replaces the user's earlier code.
/// </summary>
public sealed record CreateTelegramLinkCodeCommand(Guid UserId) : ICommand<TelegramLinkCodeInternal>;