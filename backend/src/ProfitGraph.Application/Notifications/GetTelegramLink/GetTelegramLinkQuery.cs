using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Notifications.Contracts;

namespace ProfitGraph.Application.Notifications.GetTelegramLink;

public sealed record GetTelegramLinkQuery(Guid UserId) : IQuery<TelegramLinkInternal>;