using MediatR;

using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Jobs;

/// <summary>
///     Work with a typed result, sent through the normal pipeline so validation runs again when the Worker starts it.
///     Jobs run outside any transaction; the work commits its own short transactions (ADR 0011).
/// </summary>
public interface IBackgroundJobRequest<out TResult> : IBackgroundJobRequest, IRequest<TResult>, INonTransactionalCommand
    where TResult : IBackgroundJobResult;