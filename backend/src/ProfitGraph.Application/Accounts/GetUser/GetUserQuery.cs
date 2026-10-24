using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.GetUser;

public sealed record GetUserQuery(Guid Id) : IQuery<UserContract?>;