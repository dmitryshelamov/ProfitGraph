using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.BlockUser;

public sealed record BlockUserCommand(Guid ActorId, Guid UserId) : ICommand;