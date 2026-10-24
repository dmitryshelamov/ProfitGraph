using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Jobs.Contracts;

namespace ProfitGraph.Application.Optimization.PauseOptimization;

/// <summary>
///     Pauses a queued optimization at once and asks the Worker to stop a running one after storing its trials.
/// </summary>
public sealed record PauseOptimizationCommand(Guid UserId, Guid Id) : ICommand<BackgroundJobInternal>;