using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Jobs.Contracts;

namespace ProfitGraph.Application.Jobs.CancelBackgroundJob;

public sealed record CancelBackgroundJobCommand(Guid UserId, Guid Id) : ICommand<BackgroundJobInternal>;