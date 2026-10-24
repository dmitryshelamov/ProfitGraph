using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Jobs.Contracts;

namespace ProfitGraph.Application.Jobs.GetBackgroundJob;

public sealed record GetBackgroundJobQuery(Guid UserId, Guid Id) : IQuery<BackgroundJobInternal>;