using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Jobs.Contracts;

namespace ProfitGraph.Application.Optimization.ResumeOptimization;

/// <summary>
///     Queues a paused optimization again; it continues from its stored trials.
/// </summary>
public sealed record ResumeOptimizationCommand(Guid UserId, Guid Id) : ICommand<BackgroundJobInternal>;