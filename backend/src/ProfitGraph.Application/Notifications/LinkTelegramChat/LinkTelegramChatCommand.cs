using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Notifications.LinkTelegramChat;

/// <summary>
///     Links the chat a <c>/start CODE</c> came from to the code's user; false for an unknown or expired code. A code
///     works once.
/// </summary>
public sealed record LinkTelegramChatCommand(string Code, long ChatId) : ICommand<bool>;