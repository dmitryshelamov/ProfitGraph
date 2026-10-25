using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Notifications.BlockTelegramChat;

/// <summary>
///     Telegram reported that the user blocked the bot: the chat receives nothing more until it is linked again.
/// </summary>
public sealed record BlockTelegramChatCommand(long ChatId) : ICommand;