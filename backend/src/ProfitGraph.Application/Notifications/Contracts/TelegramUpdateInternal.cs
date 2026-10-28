namespace ProfitGraph.Application.Notifications.Contracts;

/// <summary>
///     An update Telegram reports to the bot: its identifier and a text message or a button press, when it is one of those.
/// </summary>
public sealed record TelegramUpdateInternal(long UpdateId, TelegramIncomingMessageInternal? Message = null, TelegramCallbackInternal? Callback = null);