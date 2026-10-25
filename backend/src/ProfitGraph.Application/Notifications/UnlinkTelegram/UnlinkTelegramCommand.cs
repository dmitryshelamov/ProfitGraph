using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Notifications.UnlinkTelegram;

public sealed record UnlinkTelegramCommand(Guid UserId) : ICommand<bool>;