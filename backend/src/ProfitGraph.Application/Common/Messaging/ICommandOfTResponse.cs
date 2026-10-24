using MediatR;

namespace ProfitGraph.Application.Common.Messaging;

/// <summary>
///     A state change that returns a result.
/// </summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;